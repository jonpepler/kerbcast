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
// Cheap to compute (iterates Vessel.parts once with one dot/cross per
// part); call from Render() so it adapts to wind direction changes
// without needing OnVesselChanged plumbing.

using UnityEngine;

namespace Kerbcam
{
    internal struct WindwardProfile
    {
        public float WindwardRadius;
        public float ForwardStandoff;
        public float AftStandoff;

        public static WindwardProfile Compute(Vessel vessel, Vector3 windDir)
        {
            var p = new WindwardProfile { WindwardRadius = 1f, ForwardStandoff = 1f, AftStandoff = 1f };
            if (vessel == null || vessel.parts == null || vessel.parts.Count == 0) return p;
            Vector3 com = vessel.CoM;
            float fwd = 0f, aft = 0f, perpMax = 0f;
            foreach (var part in vessel.parts)
            {
                if (part == null || part.transform == null) continue;
                Vector3 rel = part.transform.position - com;
                float along = Vector3.Dot(rel, windDir);
                if (along > fwd) fwd = along;
                if (-along > aft) aft = -along;
                Vector3 perp = rel - along * windDir;
                float perpDist = perp.magnitude;
                if (perpDist > perpMax) perpMax = perpDist;
            }
            // Floors keep small probes from producing degenerate
            // (zero-sized) FX meshes.
            p.WindwardRadius = Mathf.Max(perpMax, 0.5f);
            p.ForwardStandoff = Mathf.Max(fwd, 1f);
            p.AftStandoff = Mathf.Max(aft, 1f);
            return p;
        }
    }
}
