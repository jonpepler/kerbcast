namespace Kerbcast
{
    /* Unity-free dedupe for the EVA point-of-view camera. Hullcam VDS patches an
       EVACamera (a MuMechModuleHullCamera subclass) onto every EVA kerbal part
       (HullCameraVDS/MM_Scripts/kerbalEVA.cfg), so an EVA kerbal surfaces TWO POV
       cameras: that PART camera (part-id, unlabelled, renders the kerbal model)
       AND the crew provider's KerbalFaceCamera (kerbal-id, labelled with the crew
       name). They are the same physical POV. Keep the labelled kerbal camera and
       drop the redundant part camera on the EVA kerbal part. */
    public static class EvaPovDedupe
    {
        /* Whether a discovered camera should be surfaced. A kerbal camera
           (kerbal-id namespace) is always surfaced — it is the labelled EVA POV
           (and the crew-bar facecam). A PART camera whose owning part is an EVA
           kerbal (ownerVesselIsEva) is Hullcam's redundant EVACamera and is
           dropped. Every other part camera (ownerVesselIsEva == false) is
           surfaced unchanged. */
        public static bool ShouldSurface(uint flightId, bool ownerVesselIsEva)
        {
            if (CameraId.IsKerbalId(flightId)) return true;
            return !ownerVesselIsEva;
        }
    }
}
