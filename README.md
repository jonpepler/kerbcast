# kerbcam

A from-scratch successor to OCISLY for streaming Kerbal Space Program camera feeds to a browser. Hardware-accelerated H.264 over WebRTC, with full Hullcam VDS camera-type fidelity.

## Status: pre-release, personal-use only

kerbcam isn't ready for general consumption yet.

- Linux / Steam Deck support is tier-1.
- macOS and Windows are experimental tier-2 — code paths exist, polish does not.
- Mod-platform listings (CKAN, SpaceDock, KSP Forums) will come at 1.0.

Issues and PRs are welcome, particularly if you'd like to 'adopt' macOS or Windows support.

## What it does

- Captures KSP Hullcam VDS camera feeds inside the game via `AsyncGPUReadback` — zero stall on the game's main thread
- Encodes them in hardware (libva on Linux / Steam Deck; VideoToolbox / NVENC on other tier-2 platforms) in an out-of-process 'sidecar'
- Streams them out as WebRTC media tracks — adaptive bitrate, congestion control, packet loss recovery for free
- Honours each camera's `cameraMode` so B&W / CRT / night-vision variants look like they do in the in-game Hullcam GUI
- Renders cameras only when a peer is subscribed — no idle CPU work, no in-game UI required

## Differences to OCISLY

OCISLY uses `ReadPixels` + `EncodeToJPG` on the game's main thread and ships JPEG over unary gRPC at 30 Hz. That works, but it costs real frame budget, particularly on lower powered devices, and doesn't support the visual character that Hullcam VDS encodes per part. Kerbcam aims to fix both while prioritising high performance through hardware encoding and modern Unity API use.

### Example performance

Measured on a Steam Deck running KSP 1.12 with 5 hullcams streaming (OpenGL / Mesa 22.2):

| Scenario                                        | In-game framerate (p50) |
| ----------------------------------------------- | ----------------------- |
| No camera mod                                   | 56 fps                  |
| OCISLY                                          | 9 fps                   |
| OCISLY + experimental `AsyncGPUReadback` patch  | 17 fps (+88%)           |
| Target Kerbcam (_target only - not delivered!_) | ~55 fps                 |

The patch row is a real measurement from an experimental branch on the OCISLY fork ([kerbcam-spike](https://github.com/jonpepler/OfCourseIStillLoveYou/tree/kerbcam-spike)). The full rebuild target is the no-camera-mod ceiling — once JPEG encode moves off the main thread into a hardware encoder, the per-frame cost is GPU-bound and the streaming workload stops competing with the simulation.

## Toolchain

- Plugin: C# / .NET Framework 4.8, against KSP's Unity 2019.4 LTS assemblies.
- Sidecar: Rust (stable).
- Protocol: schemas in `protocol/`, codegens to TypeScript (`.d.ts`) and C# (`.cs`).

## Companion project

[gonogo](https://github.com/jonpepler/gonogo) - a mission-control browser SPA that consumes kerbcam feeds (and a few other things). Not required to use kerbcam (any WebRTC-capable browser will do).

## TODO

- [ ] Codesign and notarise macOS sidecar binaries (Apple notarytool workflow)
- [ ] Establish SmartScreen reputation for Windows binaries
- [ ] Publish NetKAN metadata to CKAN's indexer
- [ ] Create SpaceDock listing
- [ ] KSP Forums post
- [ ] User-facing install and quickstart docs
- [ ] Telemetry / error reporting opt-in

## Future work

- [ ] Better tier-2 OS support
- [ ] Support Hullcam VDS cameras that take zoom commands
- [ ] Extend Hullcam with a pivotable camera
