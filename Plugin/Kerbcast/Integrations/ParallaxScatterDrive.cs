// Per-clone ParallaxContinued scatter drive. Parallax positions and culls its
// GPU-instanced scatters from global static state (camera position + frustum
// planes) it sets from the stock near camera, and submits the draws to the stock
// cameras. To capture scatters onto a kerbcast clone, this component (attached to
// the near clone) runs Parallax's own evaluate + submit pipeline a second time,
// pointed at the clone, inside OnPreCull, then restores the globals in
// OnPostRender. All members are reflected in by ParallaxIntegration; this
// component holds no compile-time Parallax reference.
//
// The work is gated: it does nothing unless scatters are actually loaded, and the
// underlying evaluate is an indirect compute dispatch that is zero-work-safe.

using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Kerbcast
{
    internal sealed class ParallaxScatterDrive : MonoBehaviour
    {
        // RuntimeOperations static globals.
        public FieldInfo PosField;            // Vector3 vectorCameraPos
        public FieldInfo PlanesField;         // float[] floatCameraFrustumPlanes
        // ScatterManager.
        public PropertyInfo ManagerInstance;  // static ScatterManager Instance
        public FieldInfo ActiveRenderersField; // List<ScatterRenderer> (instance)
        public MethodInfo PreRenderMethod;    // ScatterRenderer.PreRender()
        public MethodInfo RenderInCamerasMethod; // ScatterRenderer.RenderInCameras(params Camera[])
        // Quad data + per-quad evaluate.
        public FieldInfo QuadDataField;       // static Dictionary<PQ, ScatterSystemQuadData>
        public MethodInfo EvaluateQuadMethod; // ScatterSystemQuadData.EvaluateQuad()

        private Camera _cam;
        private object _savedPos;
        private object _savedPlanes;
        private bool _swapped;

        private void Awake() => _cam = GetComponent<Camera>();

        private void OnPreCull()
        {
            if (_swapped) return;
            if (!Ready()) return;
            try
            {
                object manager = ManagerInstance.GetValue(null, null);
                if (manager == null) return;
                var renderers = ActiveRenderersField.GetValue(manager) as IEnumerable;
                var quads = QuadDataField.GetValue(null) as IDictionary;
                if (renderers == null || quads == null || quads.Count == 0) return;

                // Save + overwrite the globals with this clone's view. Mark swapped
                // BEFORE overwriting so Restore() runs even if the second SetValue
                // throws between the two writes (never leave the globals dirty).
                _savedPos = PosField.GetValue(null);
                _savedPlanes = PlanesField.GetValue(null);
                _swapped = true;
                PosField.SetValue(null, _cam.transform.position);
                PlanesField.SetValue(null, PackFrustum(_cam));

                // Reset the append buffers, evaluate every loaded quad for this
                // clone's frustum, then submit the scatter draws to this clone.
                // EvaluateQuad's own guards skip empty/paused quads; the indirect
                // dispatch is zero-work-safe.
                foreach (var r in renderers) PreRenderMethod.Invoke(r, null);
                foreach (var q in quads.Values) EvaluateQuadMethod.Invoke(q, null);
                var camArg = new object[] { new Camera[] { _cam } };
                foreach (var r in renderers) RenderInCamerasMethod.Invoke(r, camArg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Kerbcast-Parallax] drive failed: {ex.Message}");
                Restore(); // never leave the globals pointing at our clone
            }
        }

        private void OnPostRender() => Restore();
        private void OnDisable() => Restore();

        private void Restore()
        {
            if (!_swapped) return;
            try
            {
                PosField.SetValue(null, _savedPos);
                PlanesField.SetValue(null, _savedPlanes);
            }
            catch (Exception ex) { Debug.LogError($"[Kerbcast-Parallax] restore failed: {ex.Message}"); }
            finally { _swapped = false; }
        }

        private bool Ready()
        {
            return _cam != null && PosField != null && PlanesField != null
                && ManagerInstance != null && ActiveRenderersField != null
                && PreRenderMethod != null && RenderInCamerasMethod != null
                && QuadDataField != null && EvaluateQuadMethod != null;
        }

        // Pack the camera's 6 frustum planes into 24 floats (normal.xyz + distance
        // per plane), the layout Parallax's compute shader reads.
        private static float[] PackFrustum(Camera cam)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var f = new float[24];
            for (int i = 0; i < 6; i++)
            {
                f[i * 4 + 0] = planes[i].normal.x;
                f[i * 4 + 1] = planes[i].normal.y;
                f[i * 4 + 2] = planes[i].normal.z;
                f[i * 4 + 3] = planes[i].distance;
            }
            return f;
        }
    }
}
