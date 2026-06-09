// Windward profile of a vessel relative to the wind axis. Used by the
// bowshock, trail, and embers effects to size and position their meshes
// adaptively — so the FX adapts to the vessel's orientation (broadside
// reentry shows a wide+close bowshock and a wide+short wake; end-on
// shows a narrow+far bowshock and a narrow+long wake).
//
// WindwardRadius is the max perpendicular-to-wind distance from CoM to any
// part — drives the dome radius / trail tube radius / ember emit spread.
// ForwardStandoff is the max along +windDir — where the bowshock dome's
// base sits relative to the vessel CoM. AftStandoff is the max along
// -windDir — where the trail tube and embers anchor.
//
// Measures part RENDERER BOUNDS, not part transform positions — a part's
// origin sits near its centre, so position-only measurement underestimated
// every extent by half a part and the FX meshes sat inside the hull (the
// CI preview harness always measured bounds; this matches it). Per part a
// local-space box is computed once (lazily, from merged renderer bounds)
// and cached; per frame only its 8 corners are projected against the wind
// axis. A same-frame memo collapses the bowshock+trail double call.

using System.Collections.Generic;
using UnityEngine;

namespace Kerbcam
{
    internal struct WindwardProfile
    {
        public float WindwardRadius;
        public float ForwardStandoff;
        public float AftStandoff;
        // Elliptical cross-section perpendicular to the wind: MajorAxis is
        // the direction (⊥ wind) of the vessel's longest perpendicular
        // extent, RadiusMajor/RadiusMinor the extents along it and along
        // wind × MajorAxis. A broadside vessel presents a long flat
        // silhouette — FX shaped as circles of WindwardRadius read as if
        // the vessel were flying nose-first. End-on the two radii converge
        // and the ellipse degenerates to the old circle.
        public Vector3 MajorAxis;
        public float RadiusMajor;
        public float RadiusMinor;

        public Vector3 MinorAxis(Vector3 windDir)
        {
            Vector3 minor = Vector3.Cross(windDir, MajorAxis);
            return minor.sqrMagnitude > 1e-6f ? minor.normalized : Vector3.up;
        }

        private struct PartBox
        {
            public Vector3 Centre;   // part-local
            public Vector3 Extents;  // part-local, axis-aligned
        }

        private static readonly Dictionary<Part, PartBox> _boxCache = new Dictionary<Part, PartBox>();
        private static float _lastCacheFlush;

        // Same-frame memo — bowshock and trail both compute the profile for
        // the same vessel/wind every frame.
        private static Vessel _memoVessel;
        private static Vector3 _memoWind;
        private static int _memoFrame = -1;
        private static WindwardProfile _memo;

        // Perp-plane corner components from the last pass — reused (no alloc
        // after warmup) for the second pass that measures the minor radius
        // once the major direction is known.
        private static readonly List<Vector3> _perpScratch = new List<Vector3>(256);

        public static WindwardProfile Compute(Vessel vessel, Vector3 windDir)
        {
            var p = new WindwardProfile
            {
                WindwardRadius = 1f, ForwardStandoff = 1f, AftStandoff = 1f,
                MajorAxis = Vector3.right, RadiusMajor = 1f, RadiusMinor = 1f,
            };
            if (vessel == null || vessel.parts == null || vessel.parts.Count == 0) return p;

            if (Time.frameCount == _memoFrame && ReferenceEquals(vessel, _memoVessel)
                && (windDir - _memoWind).sqrMagnitude < 1e-6f)
            {
                return _memo;
            }

            // Destroyed parts would otherwise pin the cache forever; a
            // periodic flush is cheaper than per-part liveness checks.
            if (Time.time - _lastCacheFlush > 60f)
            {
                _boxCache.Clear();
                _lastCacheFlush = Time.time;
            }

            Vector3 com = vessel.CoM;
            float fwd = 0f, aft = 0f, perpMax = 0f;
            Vector3 majorDir = Vector3.zero;
            _perpScratch.Clear();
            foreach (var part in vessel.parts)
            {
                if (part == null || part.transform == null) continue;
                if (!_boxCache.TryGetValue(part, out var box))
                {
                    box = ComputeLocalBox(part);
                    _boxCache[part] = box;
                }

                Transform tr = part.transform;
                Vector3 c = tr.TransformPoint(box.Centre);
                Vector3 ax = tr.right * box.Extents.x;
                Vector3 ay = tr.up * box.Extents.y;
                Vector3 az = tr.forward * box.Extents.z;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c
                        + ((i & 1) == 0 ? -ax : ax)
                        + ((i & 2) == 0 ? -ay : ay)
                        + ((i & 4) == 0 ? -az : az);
                    Vector3 rel = corner - com;
                    float along = Vector3.Dot(rel, windDir);
                    if (along > fwd) fwd = along;
                    if (-along > aft) aft = -along;
                    Vector3 perp = rel - along * windDir;
                    _perpScratch.Add(perp);
                    float perpDist = perp.magnitude;
                    if (perpDist > perpMax)
                    {
                        perpMax = perpDist;
                        majorDir = perp;
                    }
                }
            }
            // Minor radius: extent along wind × major across all corners.
            // Falls back to the circle when the major direction is
            // degenerate (vessel centred dead-on the wind axis).
            float minorMax = perpMax;
            if (majorDir.sqrMagnitude > 1e-6f)
            {
                Vector3 majorAxis = majorDir.normalized;
                Vector3 minorAxis = Vector3.Cross(windDir, majorAxis);
                if (minorAxis.sqrMagnitude > 1e-6f)
                {
                    minorAxis.Normalize();
                    minorMax = 0f;
                    for (int i = 0; i < _perpScratch.Count; i++)
                    {
                        float d = Mathf.Abs(Vector3.Dot(_perpScratch[i], minorAxis));
                        if (d > minorMax) minorMax = d;
                    }
                }
                p.MajorAxis = majorAxis;
            }
            // Floors keep small probes from producing degenerate
            // (zero-sized) FX meshes.
            p.WindwardRadius = Mathf.Max(perpMax, 0.5f);
            p.ForwardStandoff = Mathf.Max(fwd, 1f);
            p.AftStandoff = Mathf.Max(aft, 1f);
            p.RadiusMajor = Mathf.Max(perpMax, 0.5f);
            p.RadiusMinor = Mathf.Max(minorMax, 0.4f);

            _memoVessel = vessel;
            _memoWind = windDir;
            _memoFrame = Time.frameCount;
            _memo = p;
            return p;
        }

        // Merged renderer bounds expressed in the part's local frame. The
        // world AABB → local AABB conversion is conservative (box of a box),
        // which errs toward FX sitting slightly outside the hull — the
        // right direction; inside means clipping.
        private static PartBox ComputeLocalBox(Part part)
        {
            Transform tr = part.transform;
            bool any = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            var renderers = part.GetComponentsInChildren<Renderer>(includeInactive: false);
            for (int i = 0; i < renderers.Length; i++)
            {
                var rend = renderers[i];
                if (rend == null || !rend.enabled) continue;
                if (!(rend is MeshRenderer || rend is SkinnedMeshRenderer)) continue;

                Bounds b = rend.bounds;
                Vector3 cLocal = tr.InverseTransformPoint(b.center);
                Vector3 ex = tr.InverseTransformDirection(Vector3.right) * b.extents.x;
                Vector3 ey = tr.InverseTransformDirection(Vector3.up) * b.extents.y;
                Vector3 ez = tr.InverseTransformDirection(Vector3.forward) * b.extents.z;
                Vector3 eLocal = new Vector3(
                    Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x),
                    Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y),
                    Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z));

                Vector3 lo = cLocal - eLocal;
                Vector3 hi = cLocal + eLocal;
                if (!any) { min = lo; max = hi; any = true; }
                else
                {
                    min = Vector3.Min(min, lo);
                    max = Vector3.Max(max, hi);
                }
            }
            if (!any) return new PartBox { Centre = Vector3.zero, Extents = Vector3.zero };
            return new PartBox { Centre = (min + max) * 0.5f, Extents = (max - min) * 0.5f };
        }
    }
}
