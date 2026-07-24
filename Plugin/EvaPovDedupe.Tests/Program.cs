using System;
using System.Collections.Generic;
using System.Linq;
using Kerbcast;

int failures = 0;
void Check(bool cond, string msg)
{
    Console.WriteLine((cond ? "ok   " : "FAIL ") + msg);
    if (!cond) failures++;
}

// An EVA kerbal surfaces TWO POV cameras: Hullcam VDS's EVACamera on the EVA
// kerbal PART (part-id, unlabelled) AND the crew provider's KerbalFaceCamera
// (kerbal-id, labelled with the crew name). They are the same physical POV;
// dedupe to the labelled kerbal camera.
uint partEvaPov = CameraId.Synthetic(1234u, 0, "EVACam");     // part id (top-bit clear)
uint kerbalEvaPov = CameraId.KerbalWireId("Jebediah Kerman"); // kerbal id (top-bit set)

var candidates = new (uint id, bool ownerVesselIsEva)[]
{
    (partEvaPov, true),    // Hullcam EVACamera on the EVA kerbal
    (kerbalEvaPov, true),  // the labelled KerbalFaceCamera
};
var kept = candidates
    .Where(c => EvaPovDedupe.ShouldSurface(c.id, c.ownerVesselIsEva))
    .Select(c => c.id)
    .ToList();
Check(kept.Count == 1, "EVA kerbal: exactly one POV camera surfaces");
Check(kept.Count == 1 && kept[0] == kerbalEvaPov,
    "the surviving POV is the labelled kerbal camera (part EVACamera dropped)");

// A seated (non-EVA) kerbal camera is unaffected.
Check(EvaPovDedupe.ShouldSurface(CameraId.KerbalWireId("Bob Kerman"), false),
    "seated kerbal camera surfaces");
// A normal part camera is unaffected.
Check(EvaPovDedupe.ShouldSurface(CameraId.Synthetic(42u, 0, "FwdCam"), false),
    "normal part camera surfaces");
// The EVA FACEcam (crew bar) is a kerbal camera and is never dropped, even
// though the kerbal is on EVA.
Check(EvaPovDedupe.ShouldSurface(CameraId.KerbalWireId("Valentina Kerman"), true),
    "EVA facecam (kerbal camera) is untouched on EVA");
// A part camera on a non-EVA vessel is never dropped (belt-and-braces).
Check(EvaPovDedupe.ShouldSurface(CameraId.Synthetic(7u, 1, "AftCam"), false),
    "part camera on a normal vessel surfaces");

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
