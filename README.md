# kerbcam

A from-scratch successor to OCISLY for streaming Kerbal Space Program camera feeds to a browser. Hardware-accelerated H.264 over WebRTC, with full Hullcam VDS camera-type fidelity.

## Status: pre-release, personal-use only

kerbcam isn't ready for general consumption yet.

- Linux / Steam Deck support is tier-1.
- macOS and Windows are experimental tier-2 — code paths exist, polish does not.
- Mod-platform listings (CKAN, SpaceDock, KSP Forums) are intentionally deferred until reliability is proven on the author's personal install for an extended period.

Stars and issues welcome. PRs that change architecture without prior discussion will be politely declined.

## What it does (when it works)

- Captures KSP Hullcam VDS camera feeds inside the game via `AsyncGPUReadback` — zero stall on the game's main thread.
- Encodes them in hardware (libva on Linux / Steam Deck; VideoToolbox / NVENC on other tier-2 platforms) in an out-of-process sidecar.
- Streams them out as WebRTC media tracks — adaptive bitrate, congestion control, packet loss recovery come along for the ride.
- Honours each camera's `cameraMode` so B&W / CRT / night-vision variants look like they do in the in-game Hullcam GUI.
- Renders cameras only when a peer is subscribed — no idle CPU work.

## Why not just use OCISLY

OCISLY uses `ReadPixels` + `EncodeToJPG` on the game's main thread and ships JPEG over unary gRPC at 30 Hz. That works, but it costs the Steam Deck real frame budget and ignores the visual character that Hullcam VDS encodes per part. kerbcam fixes both. The full motivation, design, and trade-off analysis lives in the consumer project's [rebuild design doc](../gonogo/local_docs/ocisly_state_and_rebuild.md).

## Topology this is built for

- KSP runs on a Steam Deck (AMD Van Gogh APU, VCN 2.0 hardware H.264).
- A browser on a MacBook Pro M4 displays the feeds via WebRTC.
- LAN by default; no public TURN required for the common case.

## Toolchain

- Plugin: C# / .NET Framework 4.8, against KSP's Unity 2019.4 LTS assemblies.
- Sidecar: Rust (stable).
- Protocol: schemas in `protocol/`, codegens to TypeScript (`.d.ts`) and C# (`.cs`).

## Companion project

[gonogo](../gonogo) — the mission-control browser SPA that consumes kerbcam feeds. Not required to use kerbcam (any WebRTC-capable browser will do once the protocol is documented), but it's where the design context for this mod was hammered out.

## TODO before public release

- [ ] Codesign and notarise macOS sidecar binaries (Apple notarytool workflow)
- [ ] Establish SmartScreen reputation for Windows binaries
- [ ] Publish NetKAN metadata to CKAN's indexer
- [ ] Create SpaceDock listing
- [ ] KSP Forums announcement post
- [ ] User-facing install and quickstart docs
- [ ] Telemetry / error reporting opt-in
- [ ] Author-personal-install ≥ 1 month soak with no reliability regressions

## License

TBD — likely MIT or Apache-2.0. Will be set before any public distribution.
