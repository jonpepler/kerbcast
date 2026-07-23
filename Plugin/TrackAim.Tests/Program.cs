using System;
using Kerbcast;

int failures = 0;
void Check(bool cond, string msg)
{
    Console.WriteLine((cond ? "ok   " : "FAIL ") + msg);
    if (!cond) failures++;
}

// ShouldAim: a real mode drives the aim ONLY on a pan+zoom camera.
Check(
    TrackAim.ShouldAim(TrackAim.ModeActiveVessel, true, true),
    "active-vessel + pan + zoom -> aim");
Check(
    TrackAim.ShouldAim(TrackAim.ModeTarget, true, true),
    "target + pan + zoom -> aim");

// The pan+zoom gate: missing either capability suppresses tracking.
Check(!TrackAim.ShouldAim(TrackAim.ModeActiveVessel, true, false), "no zoom -> no aim");
Check(!TrackAim.ShouldAim(TrackAim.ModeActiveVessel, false, true), "no pan -> no aim");
Check(!TrackAim.ShouldAim(TrackAim.ModeTarget, false, false), "neither -> no aim");

// THE kOS-untouched / zero-effect guarantee: mode none never aims, whatever the
// capabilities. A later change that regresses this fails here.
Check(!TrackAim.ShouldAim(TrackAim.ModeNone, true, true), "mode none -> no aim (pan+zoom)");
Check(!TrackAim.ShouldAim(TrackAim.ModeNone, false, false), "mode none -> no aim (neither)");

// FovForDistance (DISTINCT auto-zoom primitive): closer = wider, farther =
// narrower, clamped to [fovMin, fovMax].
float near = TrackAim.FovForDistance(500f, 10f, 90f, 1000f, 40f);
float far = TrackAim.FovForDistance(2000f, 10f, 90f, 1000f, 40f);
Check(near > far, "closer target -> wider fov than a farther one");
Check(
    Math.Abs(TrackAim.FovForDistance(1000f, 10f, 90f, 1000f, 40f) - 40f) < 0.001f,
    "at the reference distance fov == reference fov");
Check(
    TrackAim.FovForDistance(1f, 10f, 90f, 1000f, 40f) == 90f,
    "very close clamps to fovMax");
Check(
    TrackAim.FovForDistance(1_000_000f, 10f, 90f, 1000f, 40f) == 10f,
    "very far clamps to fovMin");
Check(
    TrackAim.FovForDistance(0f, 10f, 90f, 1000f, 40f) == 90f,
    "degenerate zero distance -> fovMax (no divide-by-zero)");

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
