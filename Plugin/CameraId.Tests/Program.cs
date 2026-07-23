using System;
using Kerbcast;

int failures = 0;
void Check(bool cond, string msg)
{
    Console.WriteLine((cond ? "ok   " : "FAIL ") + msg);
    if (!cond) failures++;
}

const uint TopBit = 0x80000000u;

// Module 0 uses the flightID (masked to 31 bits), name ignored.
Check(CameraId.Synthetic(100u, 0, "fwd") == 100u, "module 0 == baseId (top-bit clear)");
Check(CameraId.Synthetic(100u, 0, "aft") == 100u, "module 0 ignores cameraName");
// A flightID with the top bit set is masked into the part namespace.
Check(CameraId.Synthetic(0x80000001u, 0, "") == 1u, "module 0 masks top bit");
// Modules 1+ are deterministic and differ from the base id.
Check(CameraId.Synthetic(100u, 1, "fwd") == CameraId.Synthetic(100u, 1, "fwd"), "module 1 stable");
Check(CameraId.Synthetic(100u, 1, "fwd") != 100u, "module 1 != baseId");
// Base id and camera name both perturb the hash.
Check(CameraId.Synthetic(100u, 1, "fwd") != CameraId.Synthetic(101u, 1, "fwd"), "baseId perturbs");
Check(CameraId.Synthetic(100u, 1, "fwd") != CameraId.Synthetic(100u, 1, "aft"), "cameraName perturbs");

// PART ids always have the top bit CLEAR — even for full-32-bit flightIDs and
// multi-camera hashes (KSP flightIDs are Guid hashes, top bit set ~half the time).
Check((CameraId.Synthetic(0xFFFFFFFFu, 0, "") & TopBit) == 0, "part module 0 top-bit clear");
Check((CameraId.Synthetic(0xFFFFFFFFu, 3, "cam") & TopBit) == 0, "part module 3 top-bit clear");
for (uint b = 1; b < 5000; b += 7)
    if ((CameraId.Synthetic(b * 2654435761u, (int)(b % 4), "c") & TopBit) != 0)
        Check(false, $"part id top-bit set for b={b}");

// KERBAL ids always have the top bit SET, and are derived from the stable name.
Check((CameraId.KerbalWireId("Jebediah Kerman") & TopBit) != 0, "kerbal id top-bit set");
Check((CameraId.KerbalWireId("") & TopBit) != 0, "kerbal id top-bit set (empty)");
// Same name -> same id (continuity across seat<->EVA); different name -> different id.
Check(CameraId.KerbalWireId("Jebediah Kerman") == CameraId.KerbalWireId("Jebediah Kerman"), "same name -> same id");
Check(CameraId.KerbalWireId("Jebediah Kerman") != CameraId.KerbalWireId("Bill Kerman"), "different name -> different id");
// Namespaces are disjoint by construction: a kerbal id can never equal a part id.
Check(CameraId.KerbalWireId("Jebediah Kerman") != CameraId.Synthetic(100u, 0, ""), "kerbal disjoint from part (module 0)");
Check(CameraId.KerbalWireId("Bob Kerman") != CameraId.Synthetic(0xFFFFFFFFu, 2, "cam"), "kerbal disjoint from part (module 2)");

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
