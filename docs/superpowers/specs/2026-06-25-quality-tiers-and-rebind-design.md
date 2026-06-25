# Quality tiers + rebind robustness — design

Date: 2026-06-25
Status: approved (brainstorm), pending implementation plan
Branch: `wip/quality-tiers`

## Background

Two independent resolution/bitrate knobs (`Width`/`Height` and `BitrateBps` in
`settings.cfg`) can be set inconsistently and confuse operators. The owner wants
one "quality tier" that bundles them, the ability to opt into HD when the machine
can handle it, and the existing adaptive ladder to protect framerate. Separately,
a live test surfaced that the web swallows the sidecar's `Error` reply when a
`set-quality` targets a flightId that no longer exists (after a fresh launch /
different craft), leaving the user with no feedback.

Key fact that shaped the design: kerbcam's `FlightId` **is** KSP's
`Part.flightID` (`KerbcamCore.cs` ~285-294; module 0 uses it directly, extra
Hullcam modules on a part get a deterministic hash of `(flightID, cameraName)`).
`Part.flightID` is stable across **docking, scene changes, and save/load** — it
only changes when new part instances are created (fresh launch / loading a
different craft; revert-to-launch likely). This makes flightId a good, stable
key and means an elaborate logical-identity scheme is unnecessary.

## Goals

- Replace the two knobs with one `MaxQuality` tier preset that bundles resolution
  and bitrate, while keeping raw knobs as advanced overrides (backward compat).
- Allow HD (720p) and experimental Full HD (1080p) ceilings.
- Default install behavior unchanged (SD), with the adaptive ladder on by default
  so opting into a higher ceiling is self-protecting.
- Make the `set-quality` rebind case robust: surface the sidecar's `Error`, and
  show a clear reconnecting/gone state when a tile's flightId disappears.

## Non-goals

- Bidirectional auto-ramp / hardware capability probing (operator sets the max;
  the ladder only degrades below it).
- Tier-aware protocol/web header (no wire change). Possible later enhancement.
- Logical `(vesselName, cameraName)` identity remap (flightId is already stable
  across the common churn; rejected as worse for docking).

## Design

### 1. Quality tiers

A single named value bundling resolution + bitrate. Mapping lives **plugin-side
only** (the plugin reads `settings.cfg`, allocates the ring, and spawns the
sidecar, so it already owns these values; the sidecar and protocol stay unaware
of "tiers").

| `MaxQuality` | Resolution | Bitrate |
|---|---|---|
| `low` | 640×360 | 2 Mbps |
| `sd` (default) | 1024×576 | 4 Mbps |
| `hd` | 1280×720 | 6 Mbps |
| `fullhd` | 1920×1080 | 10 Mbps (experimental) |

(Values are the starting proposal; final numbers can be tuned during
implementation against the perf baseline.)

### 2. Settings model

- New `MaxQuality` key in `settings.cfg` (default `sd`). It resolves to the
  operator ceiling resolution **and** the default bitrate.
- Explicit `Width`/`Height`/`BitrateBps` keys, **if present, override** the
  tier-derived values (advanced control + backward compatibility for existing
  configs). This is the documented precedence: explicit raw value > tier.
- Resolution lives in `KerbcamSettings` (today's `Width`/`Height` ~64-65,
  `BitrateBps` ~74, load path ~355-356). The tier→values resolution happens once
  at settings load so the rest of the code keeps seeing concrete `Width`/`Height`/
  `BitrateBps`.

### 3. Adaptive ladder on by default

- Flip `AdaptiveQuality` default to `true`. The existing
  `AdaptiveQualityController` + `ShedTable` (`KerbcamCamera.cs` ~952-960) degrade
  *below* the configured `MaxQuality` ceiling (never above) when KSP fps dips
  under `MinKspFps` / the frame budget. This is what makes a higher ceiling safe.
- This is an out-of-box behavior change (previously off); called out for the
  changelog.

### 4. Plugin↔sidecar dimension lockstep (the one real risk)

The plugin allocates each ring at the tier-resolved max dims
(`KerbcamCamera.cs` ~386, `MmapFrameRing.Create`) AND must spawn the sidecar with
**matching** max dims, or the sidecar's ring-header validation
(`sidecar/src/shared_mem/mmap.rs` ~244-253, ~303) rejects every frame. The
tier-resolved dims must be the single source feeding both the ring allocation and
the `KerbcamSidecarHost` sidecar launch args. A test must assert these cannot
drift.

### 5. Rebind robustness (web/client-sdk, flightId keying retained)

- Keep tiles and per-camera settings keyed by `flightId` (already survives
  docking, scene changes, save/load).
- **Surface the sidecar `Error` reply** that the client currently swallows, so a
  `set-quality`/`set-fov` to a stale flightId is visible (logged + a non-fatal UI
  signal), not silent.
- When a tile's `flightId` disappears from the camera snapshot (fresh launch /
  different craft), show a **"camera reconnecting / gone"** tile state rather than
  a dead/blank tile.
- No protocol change — control messages still go by flightId.

### 6. Unchanged by design

- Protocol/wire formats and the sidecar encode path: untouched.
- The browser viewer-quality menu (Full / ¾ / ½ / ¼) keeps working: it scales
  *relative* to the operator ceiling, so at an HD ceiling "Full" auto-becomes
  1280×720 (`CameraFeed.tsx` `presetDim` reads the operator dims).

### 7. Memory

Rings allocate at max dims regardless of active resolution (4 slots/camera):
~14 MB/cam at HD, ~32 MB/cam at Full HD. HD is comfortable on the Deck; Full HD is
flagged experimental (8 cameras ≈ 253 MB shared memory). No slot-count change in
v1; document the cost.

## Backward compatibility & migration

- Existing `settings.cfg` with explicit `Width`/`Height`/`BitrateBps` keeps
  working unchanged (override path), so upgraders see no behavior change.
- Fresh installs default to `MaxQuality = sd` = today's 1024×576 / 4 Mbps.
- No localStorage migration needed (web keeps flightId keying).

## Testing strategy

New tests required (per the development goal):

- **Plugin (C#):** tier→{resolution,bitrate} resolution; raw-override precedence
  (explicit Width/Height/BitrateBps wins over tier); default is `sd`; the
  ring-allocation dims and the sidecar launch-arg dims derive from the same
  resolved source (lockstep guard); `AdaptiveQuality` default is `true`.
- **Web/client-sdk:** the sidecar `Error` reply is surfaced (not swallowed); a
  tile whose flightId leaves the snapshot enters the reconnecting/gone state.
- **Live (Deck):** HD opt-in streams end-to-end via libva; adaptive degrades
  under load; docking/scene-change preserves tiles + settings (flightId stable).

## Known limitations / to verify

- Revert-to-launch flightId behavior is assumed (not yet confirmed) to create new
  part instances; if it preserves flightID, even more is covered for free. Verify
  during implementation.
- Software-encode (tier-2) at HD is heavier; the adaptive ladder is the mitigation.
- Bitrate is fixed at session init (no live REMB adaptation yet); the tier sets a
  sensible fixed value per resolution.

## Out of scope (separate follow-ups)

- Tier-aware web header (protocol approach B).
- Live bitrate adaptation (REMB).
- Per-camera tier overrides beyond the existing per-camera Width/Height cap.
