// TUFX (TexturesUnlimitedFX) post-processing for kerbcast's capture cameras.
//
// kerbcast renders each Hullcam view through its own offscreen camera stack, so
// the post-processing the player configures in TUFX's in-game UI does not reach
// the stream unless we attach it to our cameras ourselves. This wires a
// PostProcessLayer + an empty global PostProcessVolume onto each capture camera.
// The capture camera then inherits the player's active TUFX profile through
// TUFX's own global volume in the scene, so the composited frame carries the
// same tonemap / bloom / colour grading the player sees. Without it, Kerbin's
// wide-dynamic-range horizon clips dark even with HDR on (the "dark Kerbin"
// look in early streaming tests).
//
// Everything here is reflection-only: kerbcast has no compile-time reference to
// TUFX. We locate the "TUFX" assembly at runtime; if it is absent or a version
// we do not recognise, every method no-ops and the plugin runs unaffected.

using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Kerbcast
{
    internal sealed class TUFXIntegration : ICameraModIntegration
    {
        private const string LogTag = "[Kerbcast-TUFX]";

        // Resolved once on first use; null until then. _ready gates every
        // public entry point so a failed probe disables the feature cleanly.
        private bool _probed;
        private bool _ready;

        private Type _layerType;     // PostProcessLayer
        private Type _volumeType;    // PostProcessVolume
        private MethodInfo _layerInit;        // PostProcessLayer.Init(resources)
        private FieldInfo _layerVolumeMask;   // PostProcessLayer.volumeLayer
        private PropertyInfo _loaderResources; // TexturesUnlimitedFXLoader.Resources (static)
        private FieldInfo _volumeIsGlobal;    // PostProcessVolume.isGlobal
        private FieldInfo _volumePriority;    // PostProcessVolume.priority

        public string Name => "TUFX";

        public bool IsAvailable
        {
            get { Probe(); return _ready; }
        }

        public bool ForcesNoMsaa => false;

        public bool NeedsPerFrame => false;

        // Near layer ONLY. All four layers render into one shared capture texture
        // (galaxy -> scaled -> far -> near) and near is last, so a PostProcessLayer
        // on the near clone runs its post pass once over the finished composite -
        // giving the whole frame the player's look in a single pass. Applying TUFX
        // to every layer instead made each layer's post reprocess the shared
        // texture: later layers blacked the already-drawn galaxy, and the ambient-
        // occlusion pass ran per layer, which is what drew the dark horizontal
        // lines. One post pass on the last layer avoids both.
        public CameraLayers AppliesToLayers => CameraLayers.Near;

        // TUFX applies the same post-process stack to every layer, so the layer
        // argument is not used; kept to satisfy the contract.
        public void ApplyToLayer(Camera cam, CameraLayers layer) => ApplyToCameraInternal(cam);

        public void RemoveFromLayer(Camera cam, CameraLayers layer)
        {
            if (cam == null || !_probed) return;
            Strip(cam);
        }

        public void PerFrame(Camera cam, CameraLayers layer, in IntegrationFrameState state) { }

        private void Probe()
        {
            if (_probed) return;
            _probed = true;

            // operator opt-out: treat a disabled TUFX as "not available"
            if (!KerbcastSettings.EnableTUFX) { return; }

            try
            {
                var tufx = AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a.name == "TUFX")?.assembly;
                if (tufx == null)
                {
                    Debug.Log($"{LogTag} TUFX not installed; post-processing passthrough disabled");
                    return;
                }

                _layerType = tufx.GetType("UnityEngine.Rendering.PostProcessing.PostProcessLayer");
                _volumeType = tufx.GetType("UnityEngine.Rendering.PostProcessing.PostProcessVolume");
                var loaderType = tufx.GetType("TUFX.TexturesUnlimitedFXLoader");
                if (_layerType == null || _volumeType == null || loaderType == null)
                {
                    Debug.LogWarning($"{LogTag} expected TUFX types missing; unsupported TUFX version");
                    return;
                }

                const BindingFlags PubInst = BindingFlags.Public | BindingFlags.Instance;
                _layerInit = _layerType.GetMethod("Init", PubInst);
                _layerVolumeMask = _layerType.GetField("volumeLayer", PubInst);
                _loaderResources = loaderType.GetProperty("Resources", BindingFlags.Public | BindingFlags.Static);
                _volumeIsGlobal = _volumeType.GetField("isGlobal", PubInst);
                _volumePriority = _volumeType.GetField("priority", PubInst);

                if (_layerInit == null || _loaderResources == null || _volumeIsGlobal == null)
                {
                    Debug.LogWarning($"{LogTag} expected TUFX members missing; unsupported TUFX version");
                    return;
                }

                _ready = true;
                Debug.Log($"{LogTag} integration enabled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} probe failed: {ex.Message}");
                _ready = false;
            }
        }

        // Attach a TUFX post-process layer + empty global volume to one capture
        // camera. The empty volume is a harmless no-op; it lets the player's
        // active TUFX profile flow through TUFX's own global volume onto this
        // camera. Safe to call repeatedly: the volume/layer are reused if
        // already present.
        private void ApplyToCameraInternal(Camera camera)
        {
            if (camera == null) return;
            Probe();
            if (!_ready) return;

            try
            {
                var resources = _loaderResources.GetValue(null);
                if (resources == null)
                {
                    Debug.LogWarning($"{LogTag} TUFX resources not loaded yet; skipping {camera.name}");
                    return;
                }

                var layer = GetOrAdd(camera.gameObject, _layerType);
                _layerInit.Invoke(layer, new[] { resources });
                _layerVolumeMask?.SetValue(layer, (LayerMask)(~0));

                var volume = GetOrAdd(camera.gameObject, _volumeType);
                _volumeIsGlobal.SetValue(volume, true);
                _volumePriority?.SetValue(volume, 100);

                Debug.Log($"{LogTag} applied to {camera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} apply to {camera.name} failed: {ex.Message}");
                Strip(camera); // leave the camera clean rather than half-wired
            }
        }

        private void Strip(Camera camera)
        {
            try
            {
                if (_volumeType != null)
                {
                    var v = camera.gameObject.GetComponent(_volumeType);
                    if (v != null) UnityEngine.Object.Destroy(v);
                }
                if (_layerType != null)
                {
                    var l = camera.gameObject.GetComponent(_layerType);
                    if (l != null) UnityEngine.Object.Destroy(l);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} strip from {camera.name} failed: {ex.Message}");
            }
        }

        private Component GetOrAdd(GameObject go, Type type)
        {
            return go.GetComponent(type) ?? go.AddComponent(type);
        }
    }
}
