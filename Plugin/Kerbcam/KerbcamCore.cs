// KSPAddon entry point. Hooks into the Flight scene, scans the active
// vessel for Hullcam VDS parts, and pumps an AsyncGPUReadback per camera
// each LateUpdate. Each KerbcamCamera owns its own MmapFrameRing keyed
// by KSP's stable Part.flightID; the Rust sidecar discovers rings by
// globbing the kerbcam/ subdirectory under XDG_RUNTIME_DIR.
//
// Lifecycle:
//   - Awake:    spawn AsyncReadbackUpdater (KSP loads mod DLLs after
//               [RuntimeInitializeOnLoadMethod] would fire, so the
//               vendored yangrc updater never auto-attaches).
//               Ensure the rings directory exists, then spawn sidecar
//               pointed at that directory.
//   - GameEvents.onVesselChange: rebuild the camera list (which
//               creates/destroys the matching ring files).
//   - LateUpdate: refresh each tracked camera.
//   - OnDestroy: tear down cameras (each disposes its own ring + deletes
//               its ring file) and stop the sidecar.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HullcamVDS;
using UnityEngine;
using Yangrc.OpenGLAsyncReadback;
using Debug = UnityEngine.Debug;

namespace Kerbcam
{
    [KSPAddon(KSPAddon.Startup.Flight, once: false)]
    public sealed class KerbcamCore : MonoBehaviour
    {
        private const int RingSlots = 4;
        private static readonly string RingDir = ResolveRingDir();

        private KerbcamSettings _settings;
        private readonly List<KerbcamCamera> _cameras = new List<KerbcamCamera>();
        private Process _sidecar;

        // Rolling 60-sample FPS window for adaptive layer shedding.
        // ~1s at 60fps, ~2s at 30fps — long enough to ignore single-frame
        // hitches, short enough to react to sustained slow-downs.
        private const int FpsSamples = 60;
        private readonly float[] _fpsWindow = new float[FpsSamples];
        private int _fpsIdx;
        private int _fpsCount; // up to FpsSamples; growing-average until full
        private float _fpsAvg;
        private int _shedLevel;
        private const float ShedGalaxyBelowFps = 22f;
        private const float RestoreGalaxyAboveFps = 27f;
        private const float ShedScaledBelowFps = 12f;
        private const float RestoreScaledAboveFps = 17f;

        private static string ResolveRingDir()
        {
            // XDG_RUNTIME_DIR is the right home on Steam Deck / Linux —
            // it's a per-user tmpfs that survives the process and is
            // cleaned up at logout. Fall back to /tmp on macOS / when
            // XDG_RUNTIME_DIR isn't set (Mono returns "" not null there).
            // The kerbcam/ subdirectory namespaces our ring files so the
            // sidecar's *.ring glob doesn't pick up stray files.
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrEmpty(xdg) && Directory.Exists(xdg))
            {
                return Path.Combine(xdg, "kerbcam");
            }
            return "/tmp/kerbcam";
        }

        private void Awake()
        {
            Debug.Log("[Kerbcam] KerbcamCore.Awake — initialising");

            _settings = KerbcamSettings.Load();

            try
            {
                // yangrc's [RuntimeInitializeOnLoadMethod] hook never
                // fires for mod DLLs (KSP loads them post-init), so the
                // updater MonoBehaviour that pumps OpenGLAsyncReadbackRequest
                // never auto-attaches. Spawn it ourselves on a dedicated
                // DontDestroyOnLoad GameObject.
                if (AsyncReadbackUpdater.instance == null)
                {
                    var updaterGo = new GameObject("Kerbcam_AsyncReadbackUpdater");
                    UnityEngine.Object.DontDestroyOnLoad(updaterGo);
                    updaterGo.AddComponent<AsyncReadbackUpdater>();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Kerbcam] failed to spawn AsyncReadbackUpdater: {ex}");
            }

            try
            {
                Directory.CreateDirectory(RingDir);
                Debug.Log($"[Kerbcam] rings directory ready at {RingDir} ({RingSlots} slots × {_settings.Width}×{_settings.Height} RGBA per camera)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Kerbcam] failed to create rings directory at {RingDir}: {ex}");
                enabled = false;
                return;
            }

            GameEvents.onVesselChange.Add(OnVesselChange);
            RebuildCameraList(FlightGlobals.ActiveVessel);

            TryStartSidecar();
        }

        // Spawn the bundled sidecar binary if one is shipped alongside the
        // plugin. Missing or non-executable binary is logged at warn but
        // doesn't fail Awake — the operator can still launch the sidecar
        // manually from another shell, which is how kerbcam was originally
        // built and remains the dev workflow.
        private void TryStartSidecar()
        {
            try
            {
                if (_settings != null && !_settings.AutoSpawnSidecar)
                {
                    Debug.Log("[Kerbcam] AutoSpawnSidecar=false; sidecar must be launched manually");
                    return;
                }

                if (_sidecar != null && !_sidecar.HasExited)
                {
                    // Flight scene re-entered while a previous sidecar is
                    // still running — leave it alone.
                    return;
                }

                var binPath = ResolveSidecarBinary();
                if (binPath == null)
                {
                    Debug.LogWarning("[Kerbcam] no bundled sidecar binary found; launch ~/personal/kerbcam/sidecar manually if you need streaming");
                    return;
                }

                // CLI flags forwarded from settings.cfg. The sidecar
                // accepts every value we care about as a long-form arg,
                // so there's no config file to keep in sync on the
                // sidecar side.
                var args =
                    $"--shm-dir \"{RingDir}\" " +
                    $"--http-bind {_settings.HttpBind} " +
                    $"--max-width {_settings.Width} " +
                    $"--max-height {_settings.Height}";

                var psi = new ProcessStartInfo
                {
                    FileName = binPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                _sidecar = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _sidecar.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[Kerbcam.sidecar] {e.Data}");
                };
                _sidecar.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[Kerbcam.sidecar] {e.Data}");
                };
                _sidecar.Exited += (sender, e) =>
                {
                    Debug.LogWarning($"[Kerbcam] sidecar exited (code {_sidecar.ExitCode})");
                };

                _sidecar.Start();
                _sidecar.BeginOutputReadLine();
                _sidecar.BeginErrorReadLine();
                Debug.Log($"[Kerbcam] sidecar started pid={_sidecar.Id} from {binPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Kerbcam] failed to start sidecar: {ex}");
                _sidecar = null;
            }
        }

        private static string ResolveSidecarBinary()
        {
            // Bundled location: GameData/Kerbcam/sidecar/kerbcam-sidecar
            // KSPUtil.ApplicationRootPath is the KSP install root.
            var bundled = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData", "Kerbcam", "sidecar", "kerbcam-sidecar");
            if (File.Exists(bundled)) return bundled;
            return null;
        }

        private void StopSidecar()
        {
            if (_sidecar == null) return;
            try
            {
                if (!_sidecar.HasExited)
                {
                    _sidecar.Kill();
                    _sidecar.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Kerbcam] sidecar stop threw: {ex.Message}");
            }
            finally
            {
                _sidecar.Dispose();
                _sidecar = null;
            }
        }

        private void OnVesselChange(Vessel v)
        {
            Debug.Log($"[Kerbcam] vessel change: {(v != null ? v.vesselName : "<null>")}");
            RebuildCameraList(v);
        }

        private void RebuildCameraList(Vessel vessel)
        {
            foreach (var cam in _cameras) cam.Dispose();
            _cameras.Clear();

            if (vessel == null) return;

            foreach (var part in vessel.parts)
            {
                var hullcam = part.FindModuleImplementing<MuMechModuleHullCamera>();
                if (hullcam == null) continue;
                try
                {
                    var partName = part.partInfo?.name ?? string.Empty;
                    var initialLayers = _settings.GetInitialLayers(partName);
                    _cameras.Add(new KerbcamCamera(
                        hullcam,
                        RingDir,
                        RingSlots,
                        _settings.Width,
                        _settings.Height,
                        initialLayers));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Kerbcam] failed to attach to {part.name}: {ex}");
                }
            }
            Debug.Log($"[Kerbcam] tracking {_cameras.Count} Hullcam VDS camera(s)");
        }

        // LateUpdate so the Unity render cameras have finished compositing
        // the scene into our RenderTextures before we kick the readback.
        private void LateUpdate()
        {
            UpdateFpsAverage();
            ApplyAdaptiveShedding();

            for (int i = 0; i < _cameras.Count; i++)
            {
                _cameras[i].Refresh();
            }
        }

        private void UpdateFpsAverage()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt < 0.0001f) return;
            _fpsWindow[_fpsIdx] = 1f / dt;
            _fpsIdx = (_fpsIdx + 1) % FpsSamples;
            if (_fpsCount < FpsSamples) _fpsCount++;

            float sum = 0f;
            for (int i = 0; i < _fpsCount; i++) sum += _fpsWindow[i];
            _fpsAvg = sum / _fpsCount;
        }

        // Per-tick: decide whether to escalate / de-escalate the shed
        // level given the rolling fps average. Hysteresis between shed
        // and restore thresholds prevents flapping. Skips entirely until
        // the window has filled — early frames after a scene load are
        // noisy and would trigger spurious sheds.
        private void ApplyAdaptiveShedding()
        {
            if (_fpsCount < FpsSamples) return;

            int desired = _shedLevel;
            switch (_shedLevel)
            {
                case 0:
                    if (_fpsAvg < ShedGalaxyBelowFps) desired = 1;
                    break;
                case 1:
                    if (_fpsAvg < ShedScaledBelowFps) desired = 2;
                    else if (_fpsAvg > RestoreGalaxyAboveFps) desired = 0;
                    break;
                case 2:
                    if (_fpsAvg > RestoreScaledAboveFps) desired = 1;
                    break;
            }

            if (desired == _shedLevel) return;

            _shedLevel = desired;
            Debug.Log($"[Kerbcam] adaptive shed level={_shedLevel} (avg fps={_fpsAvg:F1})");
            foreach (var cam in _cameras) cam.ApplyAutoShed(_shedLevel);
        }

        private void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            foreach (var cam in _cameras) cam.Dispose();
            _cameras.Clear();
            StopSidecar();
        }
    }
}
