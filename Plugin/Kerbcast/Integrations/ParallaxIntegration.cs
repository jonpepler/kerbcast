// ParallaxContinued scatter capture for kerbcast's near terrain clone. Resolves
// Parallax's RuntimeOperations globals, ScatterManager, ScatterRenderer, and the
// per-quad evaluate by reflection, then attaches a ParallaxScatterDrive to the
// near clone that re-runs Parallax's evaluate + submit for the clone each frame.
// Also ORs the scatter layer (15) into the clone's cullingMask so the draws land.
//
// Targets the maintained ParallaxContinued (Parallax.RuntimeOperations). Reflection
// -only; absent or disabled Parallax no-ops.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Kerbcast
{
    internal sealed class ParallaxIntegration : ICameraModIntegration
    {
        private const string LogTag = "[Kerbcast-Parallax]";
        private const int ScatterLayer = 15;

        private bool _probed;
        private bool _ready;

        private FieldInfo _posField, _planesField, _activeRenderersField, _quadDataField;
        private PropertyInfo _managerInstance;
        private MethodInfo _preRender, _renderInCameras, _evaluateQuad;

        private readonly Dictionary<Camera, List<Component>> _added = new Dictionary<Camera, List<Component>>();
        private readonly HashSet<Camera> _maskSet = new HashSet<Camera>();

        public string Name => "Parallax";
        public bool ForcesNoMsaa => false;
        public bool NeedsPerFrame => false;
        public CameraLayers AppliesToLayers => CameraLayers.Near;

        public bool IsAvailable { get { Probe(); return _ready; } }

        private void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                if (!KerbcastSettings.EnableParallax)
                {
                    Debug.Log($"{LogTag} disabled by settings; Parallax capture off");
                    return;
                }
                var asm = AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a.name == "Parallax")?.assembly;
                var runtimeOps = asm?.GetType("Parallax.RuntimeOperations");
                var manager = asm?.GetType("Parallax.ScatterManager");
                var renderer = asm?.GetType("Parallax.ScatterRenderer");
                var component = asm?.GetType("Parallax.ScatterComponent");
                var quadData = asm?.GetType("Parallax.ScatterSystemQuadData");
                if (runtimeOps == null || manager == null || renderer == null
                    || component == null || quadData == null)
                {
                    Debug.Log($"{LogTag} ParallaxContinued not installed; capture disabled");
                    return;
                }

                const BindingFlags PubStat = BindingFlags.Public | BindingFlags.Static;
                const BindingFlags PubInst = BindingFlags.Public | BindingFlags.Instance;
                _posField = runtimeOps.GetField("vectorCameraPos", PubStat);
                _planesField = runtimeOps.GetField("floatCameraFrustumPlanes", PubStat);
                _managerInstance = manager.GetProperty("Instance", PubStat);
                _activeRenderersField = manager.GetField("activeScatterRenderers", PubInst);
                _preRender = renderer.GetMethod("PreRender", PubInst, null, Type.EmptyTypes, null);
                _renderInCameras = renderer.GetMethod("RenderInCameras", PubInst,
                    null, new[] { typeof(Camera).MakeArrayType() }, null);
                _quadDataField = component.GetField("scatterQuadData", PubStat);
                _evaluateQuad = quadData.GetMethod("EvaluateQuad", PubInst, null, Type.EmptyTypes, null);

                if (_posField == null || _planesField == null || _managerInstance == null
                    || _activeRenderersField == null || _preRender == null
                    || _renderInCameras == null || _quadDataField == null || _evaluateQuad == null)
                {
                    Debug.LogWarning($"{LogTag} expected ParallaxContinued members missing; unsupported version");
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

        public void ApplyToLayer(Camera cam, CameraLayers layer)
        {
            if (cam == null || layer != CameraLayers.Near) return;
            Probe();
            if (!_ready) return;
            try
            {
                // Let the clone see the scatter layer so the draws land on it.
                if ((cam.cullingMask & (1 << ScatterLayer)) == 0)
                {
                    cam.cullingMask |= (1 << ScatterLayer);
                    _maskSet.Add(cam);
                }

                var drive = cam.gameObject.AddComponent<ParallaxScatterDrive>();
                drive.PosField = _posField;
                drive.PlanesField = _planesField;
                drive.ManagerInstance = _managerInstance;
                drive.ActiveRenderersField = _activeRenderersField;
                drive.PreRenderMethod = _preRender;
                drive.RenderInCamerasMethod = _renderInCameras;
                drive.QuadDataField = _quadDataField;
                drive.EvaluateQuadMethod = _evaluateQuad;
                Track(cam, drive);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} apply to {cam.name} failed: {ex.Message}");
                RemoveFromLayer(cam, layer);
            }
        }

        private void Track(Camera cam, Component c)
        {
            if (!_added.TryGetValue(cam, out var list))
            {
                list = new List<Component>();
                _added[cam] = list;
            }
            list.Add(c);
        }

        public void RemoveFromLayer(Camera cam, CameraLayers layer)
        {
            if (cam == null) return;
            try
            {
                if (_added.TryGetValue(cam, out var list))
                {
                    foreach (var c in list)
                        if (c != null) UnityEngine.Object.Destroy(c);
                    _added.Remove(cam);
                }
                if (_maskSet.Remove(cam))
                    cam.cullingMask &= ~(1 << ScatterLayer);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} remove from {cam.name} failed: {ex.Message}");
            }
        }

        public void PerFrame(Camera cam, CameraLayers layer, in IntegrationFrameState state) { }
    }
}
