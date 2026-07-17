using System.Collections.Generic;
using UnityEngine;

namespace Kerbcast
{
    /* Immutable per-camera snapshot handed to the kOS add-on. Plain data +
       the owning Part; no kOS types (Kerbcast.dll must not link kOS). */
    public sealed class KerbcastCameraView
    {
        public uint FlightId;
        public uint PartFlightId;
        public string CameraName;
        public string PartName;
        public string PartTitle;
        public bool SupportsZoom;
        public bool SupportsPan;
        public float Fov, FovMin, FovMax;
        public float PanYaw, PanPitch;
        public float PanYawMin, PanYawMax, PanPitchMin, PanPitchMax;
        public global::Part Part;
    }

    /* In-process control seam the KerbcastKos add-on calls. Resolves cameras
       through the live KerbcastCore and applies FOV/pan; the plugin already
       feeds resulting state back to the sidecar via global.status.json. */
    public static class KerbcastControl
    {
        public static bool IsActive => KerbcastCore.Instance != null;

        public static IReadOnlyList<KerbcastCameraView> CamerasFor(global::Vessel vessel)
        {
            var result = new List<KerbcastCameraView>();
            var cams = KerbcastCore.Instance?.Cameras;
            if (cams == null || vessel == null) return result;
            for (int i = 0; i < cams.Count; i++)
            {
                var c = cams[i];
                if (c.Hullcam != null && c.Hullcam.vessel == vessel)
                    result.Add(ToView(c));
            }
            return result;
        }

        public static KerbcastCameraView ViewOf(uint flightId)
        {
            var c = Find(flightId);
            return c == null ? null : ToView(c);
        }

        public static bool SetFov(uint flightId, float fov)
        {
            var c = Find(flightId); if (c == null) return false; c.SetFov(fov); return true;
        }

        public static bool SetPan(uint flightId, float yaw, float pitch)
        {
            var c = Find(flightId); if (c == null) return false; c.SetPanTarget(yaw, pitch); return true;
        }

        public static bool AimAt(uint flightId, Vector3 worldPoint)
        {
            var c = Find(flightId); if (c == null) return false; c.AimAt(worldPoint); return true;
        }

        static KerbcastCamera Find(uint flightId)
        {
            var cams = KerbcastCore.Instance?.Cameras;
            if (cams == null) return null;
            for (int i = 0; i < cams.Count; i++) if (cams[i].FlightId == flightId) return cams[i];
            return null;
        }

        static KerbcastCameraView ToView(KerbcastCamera c)
        {
            return new KerbcastCameraView
            {
                FlightId = c.FlightId,
                PartFlightId = c.Hullcam != null && c.Hullcam.part != null ? c.Hullcam.part.flightID : 0u,
                CameraName = c.CameraName, PartName = c.PartName, PartTitle = c.PartTitle,
                SupportsZoom = c.SupportsZoom, SupportsPan = c.SupportsPan,
                Fov = c.Fov, FovMin = c.FovMin, FovMax = c.FovMax,
                PanYaw = c.PanYaw, PanPitch = c.PanPitch,
                PanYawMin = c.PanYawMin, PanYawMax = c.PanYawMax,
                PanPitchMin = c.PanPitchMin, PanPitchMax = c.PanPitchMax,
                Part = c.Hullcam != null ? c.Hullcam.part : null,
            };
        }
    }
}
