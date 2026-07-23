namespace Kerbcast
{
    /* Pure, Unity-free decision helpers for browser auto-track (issue #6), so
       the gating + zoom math is unit-testable without KSP / FlightGlobals. The
       KSP-bound half (resolving the world position and calling AimAt) lives in
       KerbcastCamera; this file carries only the branchless decisions and is
       compiled verbatim by the TrackAim.Tests project. */
    public static class TrackAim
    {
        /* Track-mode wire values — mirror protocol TrackMode + the control
           block's track_mode u32 (0=none, 1=active-vessel, 2=target). */
        public const int ModeNone = 0;
        public const int ModeActiveVessel = 1;
        public const int ModeTarget = 2;

        /* True when a browser track should drive the aim this frame: a real
           mode is set AND the camera can BOTH pan and zoom (issue #6 only
           surfaces tracking on pan+zoom mounts). mode==none => false, so a
           non-tracking camera pays exactly one bool check per frame and nothing
           else — the kOS aim path is completely untouched. */
        public static bool ShouldAim(int mode, bool supportsPan, bool supportsZoom)
        {
            return mode != ModeNone && supportsPan && supportsZoom;
        }

        /* DISTINCT auto-zoom primitive (spec: independently deployable, NOT
           bundled with the aim). Maps camera->target distance to a field of
           view: closer = wider, farther = narrower, via a simple angular-size
           model fov = referenceFovDeg * referenceDistance / distance, clamped to
           [fovMin, fovMax]. Kept separable so it can ride its own control gate
           later; the aim path does not call it. */
        public static float FovForDistance(
            float distanceMeters, float fovMin, float fovMax,
            float referenceDistanceMeters, float referenceFovDeg)
        {
            if (distanceMeters <= 0f || referenceDistanceMeters <= 0f) return fovMax;
            float fov = referenceFovDeg * (referenceDistanceMeters / distanceMeters);
            if (fov < fovMin) return fovMin;
            if (fov > fovMax) return fovMax;
            return fov;
        }
    }
}
