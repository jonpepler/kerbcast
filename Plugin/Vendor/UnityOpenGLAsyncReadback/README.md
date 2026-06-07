# Vendored: UnityOpenGLAsyncReadback

Vendored copy of `Yangrc.OpenGLAsyncReadback` from
https://github.com/yangrc1234/UnityOpenGLAsyncReadback (MIT licensed,
copyright 2018 Aurélien Labate and 2019 yangrc — see `LICENSE`).

Provides true asynchronous GPU→CPU readback on Unity's OpenGL backend,
where Unity's own `AsyncGPUReadback` API is a no-op
(`SystemInfo.supportsAsyncGPUReadback == false`). The public C# entry
point is
`Yangrc.OpenGLAsyncReadback.UniversalAsyncGPUReadbackRequest.Request(...)`,
which dispatches at runtime to:

- Unity's native `AsyncGPUReadback` when supported (D3D11 / Vulkan / Metal).
- The bundled `AsyncGPUReadbackPlugin` (`libAsyncGPUReadbackPlugin.so` on
  Linux, `AsyncGPUReadbackPlugin.dll` on Windows) otherwise.

## Files

- `AsyncGPUReadbackPlugin.cs` — C# wrapper + DllImports.
- `AsyncReadbackUpdater.cs` — `MonoBehaviour` that pumps pending readbacks
  every frame. `[RuntimeInitializeOnLoadMethod]` is unreliable in KSP
  (Unity loads mod DLLs after that hook fires), so the kerbcam plugin
  spawns this manually on first use.
- `LICENSE` — upstream MIT license, preserved verbatim.

## Native plugin binary

`libAsyncGPUReadbackPlugin.so`. Mono on Linux only finds it from specific paths
(`KSP_Data/MonoBleedingEdge/x86_64/` or `KSP_Data/Plugins/`); the install copies
it to those. **No longer the upstream prebuilt binary** — it is now built from
the vendored C++ source under `NativePlugin/` (kerbcam fork, see below) by
`.github/workflows/native-readback-ci.yml` on ubuntu-22.04 (glibc 2.35 floor,
matching the Deck's pressure-vessel; CI guards the requirement at ≤ 2.36).
Download the `libAsyncGPUReadbackPlugin-linux-x64` artifact and copy it to both
Mono paths above.

## Local modifications (kerbcam fork)

Upstream is unmaintained, so this copy is treated as a maintained fork. MIT
permits modification; the `LICENSE` and copyright notices are preserved
verbatim. Divergences from upstream, all clearly marked `kerbcam … (not
upstream)` in-source:

- **`AsyncGPUReadbackPlugin.cs`** — added zero-copy readback accessors so the
  OpenGL path can write the native plugin buffer straight into the frame ring,
  skipping `GetRawData`'s `Allocator.Temp` NativeArray + `MemMove` (one of two
  full-frame copies per readback, plus a per-frame Temp allocation):
  - `UniversalAsyncGPUReadbackRequest.TryGetRawPtr(out void*, out int)`
  - `OpenGLAsyncReadbackRequest.GetRawDataPtr(out void*, out int)`
  Rationale: `local_docs/perf_profiles/readback_investigation.md` change #1.
- **`AsyncReadbackUpdater.cs`** — added `OnDestroy()` that nulls the static
  `instance` (`if (instance == this) instance = null;`). Upstream never cleared
  it, so after the pump GameObject was destroyed the static held a stale
  reference and consumers re-checking `instance == null` (KerbcamCore re-spawning
  the pump on the next Flight scene) saw "not null" and never respawned — async
  readbacks then wedged until a full KSP restart.
  Rationale: `local_docs/perf_profiles/session_20260606.md` (pump-respawn bug).

- **`NativePlugin/`** — the full upstream native C++ source (pinned at upstream
  `aff05c2`), vendored so we can build a modified `.so`. Changes, marked
  `kerbcam … (not upstream)` in `src/AsyncGPUReadbackPlugin.cpp`:
  - **#2 FBO+PBO pool** — `FrameTask` reuses a size-keyed framebuffer + pixel
    buffer across readbacks instead of `glGen*`/`glBufferData`/`glDelete*` every
    frame. Render-thread-only, no extra locking (StartRequest/Update both run on
    the render thread under `tasks_mutex`).
  - **#4 format diagnostic** — logs the driver's `GL_IMPLEMENTATION_COLOR_READ_FORMAT/
    _TYPE` vs. the format we request, once, to stderr (→ Player.log). Diagnostic
    only; no format change yet (a BGRA switch would ripple into the sidecar's
    byte-order contract).
  - `CMakeLists.txt` — dropped `-Werror`/`/WX` (builds on a newer toolchain than
    upstream).
  Rationale: `local_docs/perf_profiles/readback_investigation.md` #2/#4.

Note: the `ClearDeadRefs` dictionary-during-enumeration bug is NOT fixed in
this source — it's patched at runtime via Harmony in
`Plugin/Kerbcam/AsyncReadbackRegistryFix.cs` (kept that way so that file stays
closer to upstream).

## Updating

⚠️ The `cp` below OVERWRITES the local modifications above — re-apply them
after updating (diff against git history for the exact hunks).

```sh
cd /tmp && git clone --depth 1 https://github.com/yangrc1234/UnityOpenGLAsyncReadback.git yangrc-update
cp /tmp/yangrc-update/UnityExampleProject/Assets/OpenglAsyncReadback/Scripts/{AsyncGPUReadbackPlugin,AsyncReadbackUpdater}.cs Plugin/Vendor/UnityOpenGLAsyncReadback/
cp /tmp/yangrc-update/LICENSE Plugin/Vendor/UnityOpenGLAsyncReadback/
cp /tmp/yangrc-update/UnityExampleProject/Assets/OpenglAsyncReadback/Plugins/Linux/libAsyncGPUReadbackPlugin.so <install>/GameData/Kerbcam/Plugins/x86_64/
# then re-apply the kerbcam zero-copy accessors (see "Local modifications")
```
