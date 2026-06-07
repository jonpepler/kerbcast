# kerbcam-sidecar

Per-OS hardware H.264 encoder + WebRTC peer + shared-memory frame ingest for the kerbcam KSP camera-streaming mod. Lives next to the C# Unity plugin in the parent `kerbcam` repo; the plugin spawns this binary on `Awake()` and kills it on `OnDestroy()`.

## Status

**Scaffold only.** Compiles, all unit tests pass. Encoder backends, shared-memory ring, and control protocol are stubbed; the trait surface is defined so subsequent spikes can fill in implementations one at a time without churning everything else.

See gonogo's `local_docs/ocisly_state_and_rebuild.md` for the design (§2 architecture, §3 Hullcam filter fidelity, §4 perf budgets, §8 spike plan).

## Build

```
cargo build           # debug binary
cargo build --release # production binary that the mod packages
cargo test            # all unit tests
```

## Layout

```
src/
├── lib.rs              public library surface
├── main.rs             bin entry: arg parse, logging, lifecycle
├── encoder/
│   ├── mod.rs          EncoderBackend trait + auto_select factory
│   ├── libva.rs        TIER-1 Linux/Deck (stub)
│   ├── videotoolbox.rs TIER-2 macOS (stub)
│   ├── nvenc.rs        TIER-2 Windows/NVIDIA (stub)
│   └── software.rs     Software fallback (OpenH264 / x264), stub
├── protocol/
│   └── mod.rs          ClientToSidecar + SidecarToClient + CameraMetadata
└── shared_mem/
    └── mod.rs          FrameRing skeleton (in-process for now)
```

## Next spikes

1. **libva H.264 encode of a synthetic frame.** Pick the binding crate (`ffmpeg-next` vs direct `cros-libva`), wire `Software::encode` to actually produce NAL units (start with software so the trait shape is settled), then port to libva.
2. **Shared-mem mmap.** Replace the `Mutex`-backed `FrameRing` with a real mmap-backed implementation using the `shared_memory` crate. Cross-language layout = the binary header struct in this file is the contract.
3. **WebRTC peer + one media track.** `webrtc-rs`, RTCPeerConnection, attach a synthetic NAL source. Test against a throwaway HTML page that does manual SDP exchange.
4. **Control data-channel.** Hook up the `protocol` module's message types as JSON-over-`RTCDataChannel`.
5. **KSP IPC integration.** The C# plugin starts writing to the shared-mem ring; sidecar reads from it instead of synthetic frames.

The trait + protocol shapes don't change as these land; the work is filling in the stubs.
