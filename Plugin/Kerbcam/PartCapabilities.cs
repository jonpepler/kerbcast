using System.Collections.Generic;

namespace Kerbcam
{
    internal struct PanCapability
    {
        public float YawMin;
        public float YawMax;
        public float PitchMin;
        public float PitchMax;
        /// <summary>Degrees per second during interpolation slew.</summary>
        public float SlewDegPerSec;
        /// <summary>Named transform in the part mesh that rotates for yaw.
        /// Empty string means no mesh animation for yaw.</summary>
        public string YawTransformName;
        /// <summary>Named transform in the part mesh that rotates for pitch.
        /// Empty string means no mesh animation for pitch.</summary>
        public string PitchTransformName;

        public bool SupportsPan => YawMin != YawMax || PitchMin != PitchMax;
    }

    // Hardcoded capability table keyed by KSP part name (partInfo.name).
    // Pan capability is NOT read from settings.cfg — Camera {} nodes stay
    // PartName / Layers / Width / Height only. This table is the single
    // source of truth for which parts can pan and by how much.
    internal static class PartCapabilities
    {
        public static readonly PanCapability None = default;

        private static readonly Dictionary<string, PanCapability> Table =
            new Dictionary<string, PanCapability>(System.StringComparer.Ordinal)
        {
            // TurretCam: yaw-only steerable mount. The part has a BottomJoint
            // transform (rotating head) and a TopJoint (fixed base). The original
            // author tried ModuleRoboticRotationServo here but it requires
            // PhysicsSignificance=0 and Breaking Ground DLC; the part is
            // physicsless (PhysicsSignificance=1). We rotate BottomJoint directly
            // via FindModelTransform + localRotation — same technique as solar
            // panels and landing gear. Limits from the commented-out servo block:
            // hardMinMaxLimits = -177, 177 — we trim to ±135 to leave a dead
            // zone around the mount's physical cable entry.
            ["DC.TurretCam"] = new PanCapability
            {
                YawMin = -135f, YawMax = 135f,
                PitchMin = 0f,  PitchMax = 0f,
                SlewDegPerSec = 180f,
                YawTransformName = "BottomJoint",
                PitchTransformName = "",
            },

            // Launch camera: full yaw ring + modest pitch arc. Mesh transform
            // names TBD — need an in-game diagnostic dump to identify the pivot
            // nodes. For now both are empty so the camera view pans without mesh
            // animation; add names after the in-game dump.
            ["hc.launchcam"] = new PanCapability
            {
                YawMin = -180f, YawMax = 180f,
                PitchMin = -45f, PitchMax = 60f,
                SlewDegPerSec = 90f,
                YawTransformName = "",
                PitchTransformName = "",
            },
        };

        public static PanCapability ForPart(string partName)
        {
            if (string.IsNullOrEmpty(partName)) return None;
            return Table.TryGetValue(partName, out var cap) ? cap : None;
        }
    }
}
