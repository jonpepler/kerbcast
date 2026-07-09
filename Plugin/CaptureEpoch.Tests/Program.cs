// Unit test for CaptureEpoch: the monotonic counter the status writer
// stamps into global.status.json's captureEpoch. It bumps on a KSP
// mission-time discontinuity (revert / quickload / scene reload) so a
// consumer can flush and resync; the absolute value is meaningless, only
// that it changes. Unity-free, so it runs standalone.
//
// Exit code 0 = pass, 1 = fail.

using System;
using Kerbcast;

int failures = 0;
void Check(bool cond, string msg)
{
    if (cond) Console.WriteLine("  ok   " + msg);
    else { Console.Error.WriteLine("  FAIL " + msg); failures++; }
}

var epoch = new CaptureEpoch();
uint start = epoch.Value;

// Bump advances the value.
epoch.Bump();
Check(epoch.Value == start + 1, "Bump increments the value");

// Repeated bumps stay strictly monotonic.
epoch.Bump();
epoch.Bump();
Check(epoch.Value == start + 3, "successive bumps accumulate");

// Reading without bumping does not change it.
uint held = epoch.Value;
Check(epoch.Value == held, "Value is stable between bumps");

if (failures == 0) { Console.WriteLine("CaptureEpoch: all checks passed"); return 0; }
Console.Error.WriteLine($"CaptureEpoch: {failures} check(s) failed"); return 1;
