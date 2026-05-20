// Per-Hullcam VDS tracking. Owns one Unity Camera triple (near / scaled /
// galaxy) parented to the part's transform, an AsyncGPUReadback request in
// flight at most, and writes RGBA frames into the shared mmap ring on
// each completed readback.
//
// Lifted from the architecture proven in the OCISLY-spike branch of
// jonpepler/OfCourseIStillLoveYou (TrackingCamera.cs) — same
// AsyncGPUReadback shape, same _readbackInFlight poll guard, same
// depth=0 helper RT to dodge the renderbuffer/GL_TEXTURE_2D quirk on
// Mesa OpenGL. The new bits here are (a) writing into the cross-language
// MmapFrameRing instead of stamping a JPEG over gRPC, and (b) being a
// pure-managed object rather than a TrackingCamera wrapping a GUI window.

using System;
using System.IO;
using HullcamVDS;
using UnityEngine;
using UnityEngine.Rendering;
using Yangrc.OpenGLAsyncReadback;

namespace Kerbcam
{
    internal sealed class KerbcamCamera
    {
        public uint FlightId { get; }
        public MuMechModuleHullCamera Hullcam { get; }
        public int Width { get; }
        public int Height { get; }

        private readonly Camera[] _cameras = new Camera[3];
        private readonly RenderTexture _captureRt;
        private readonly RenderTexture _readbackRt; // depth=0, GL_TEXTURE_2D-clean
        private readonly Texture2D _scratchTex;
        private readonly MmapFrameRing _ring;
        private readonly string _ringPath;

        private UniversalAsyncGPUReadbackRequest _pendingRequest;
        private bool _readbackInFlight;
        private double _pendingCaptureTsMs;
        private int _consecutiveErrors;

        public KerbcamCamera(MuMechModuleHullCamera hullcam, string ringDir, int slotCount, int width, int height)
        {
            Hullcam = hullcam;
            FlightId = hullcam.part.flightID;
            Width = width;
            Height = height;

            // Per-camera ring keyed by the part's stable flightID. Survives
            // save/reload of the same craft; unique per part on a vessel.
            _ringPath = Path.Combine(ringDir, $"{FlightId}.ring");
            _ring = MmapFrameRing.Create(_ringPath, slotCount, width, height);

            _captureRt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
            };
            _captureRt.Create();

            // depth=0 so GetNativeTexturePtr returns a vanilla GL_TEXTURE_2D
            // handle on Mesa OpenGL. With depth=24 (the capture RT) the
            // yangrc plugin's glGetTexLevelParameteriv reads back zero
            // dimensions and silently does nothing.
            _readbackRt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _readbackRt.Create();

            // RGBA32 (not ARGB32). DX11/Mesa async readback only supports a
            // narrow set of GraphicsFormats as readback destinations; RGBA32
            // (R8G8B8A8_UNorm) is in the list, ARGB32 (B8G8R8A8_SRGB) isn't.
            _scratchTex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);

            SetCameras();
        }

        // Build the near / scaled / galaxy Unity Camera triple parented to
        // the part's transform. Matches OCISLY's TrackingCamera.SetCameras
        // pattern: one camera per Unity rendering layer, all sharing the
        // same RenderTexture target so a single readback captures the full
        // composited frame.
        private void SetCameras()
        {
            var partTransform = Hullcam.part.transform.Find(Hullcam.cameraTransformName);
            if (partTransform == null)
            {
                Debug.LogWarning($"[Kerbcam] cam={FlightId} cameraTransformName '{Hullcam.cameraTransformName}' not found on part {Hullcam.part.name}");
                return;
            }

            var nearGo = new GameObject($"Kerbcam_{FlightId}_Near");
            nearGo.transform.SetParent(partTransform, worldPositionStays: false);
            nearGo.transform.localPosition = Hullcam.cameraPosition;
            nearGo.transform.localRotation = Quaternion.LookRotation(Hullcam.cameraForward, Hullcam.cameraUp);

            var nearCam = nearGo.AddComponent<Camera>();
            nearCam.clearFlags = CameraClearFlags.Depth;
            nearCam.cullingMask = ~((1 << 9) | (1 << 10) | (1 << 23)); // exclude scaled / galaxy / UI
            nearCam.fieldOfView = Hullcam.cameraFoV;
            nearCam.nearClipPlane = Hullcam.cameraClip;
            nearCam.targetTexture = _captureRt;
            _cameras[0] = nearCam;

            // Skipping the scaled+galaxy cameras for the v0.1 spike — Hullcam's
            // own MovieTime instantiates those when its in-game GUI opens a
            // camera, and replicating that path is its own port. v0.1 ships
            // just the near-camera capture; scaled/galaxy come online once we
            // verify the near path produces real frames end-to-end.

            foreach (var cam in _cameras)
            {
                if (cam != null) cam.enabled = true;
            }
        }

        public void Refresh()
        {
            // Poll: drain a completed readback before issuing a new one.
            if (_readbackInFlight)
            {
                if (!_pendingRequest.done) return;
                ProcessReadback(_pendingRequest);
                _readbackInFlight = false;
            }

            try
            {
                // Blit the depth-bundled capture RT into the clean readback RT.
                Graphics.Blit(_captureRt, _readbackRt);

                _readbackInFlight = true;
                _pendingCaptureTsMs = Time.unscaledTime * 1000.0;
                _pendingRequest = UniversalAsyncGPUReadbackRequest.Request(_readbackRt, 0);
            }
            catch (Exception ex)
            {
                _readbackInFlight = false;
                LogRateLimited($"capture pipeline threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void ProcessReadback(UniversalAsyncGPUReadbackRequest request)
        {
            try
            {
                if (request.hasError)
                {
                    LogRateLimited("AsyncGPUReadback returned hasError");
                    return;
                }

                var data = request.GetData<byte>();
                _scratchTex.LoadRawTextureData(data);
                _scratchTex.Apply();
                var rgba = _scratchTex.GetRawTextureData();

                _ring.Produce(Width, Height, _pendingCaptureTsMs, rgba, 0, rgba.Length);
                _consecutiveErrors = 0;
            }
            catch (Exception ex)
            {
                LogRateLimited($"readback callback threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void LogRateLimited(string message)
        {
            // 1-in-300 frames at 30fps = log at most once per 10s per camera.
            if (_consecutiveErrors == 0 || _consecutiveErrors % 300 == 0)
            {
                Debug.Log($"[Kerbcam] cam={FlightId} {message}");
            }
            _consecutiveErrors++;
        }

        public void Dispose()
        {
            foreach (var cam in _cameras)
            {
                if (cam != null) UnityEngine.Object.Destroy(cam.gameObject);
            }
            if (_captureRt != null) _captureRt.Release();
            if (_readbackRt != null) _readbackRt.Release();
            UnityEngine.Object.Destroy(_scratchTex);

            _ring?.Dispose();
            // Best-effort: drop the ring file so the sidecar's directory
            // scan stops surfacing this camera once it's gone.
            try
            {
                if (File.Exists(_ringPath)) File.Delete(_ringPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Kerbcam] cam={FlightId} ring file delete failed: {ex.Message}");
            }
        }
    }
}
