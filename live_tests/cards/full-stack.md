# Full-stack integration test card (Tier 1, primary)

The realistic player config: every visual mod installed and every kerbcast
integration on at once. This is the first and most valuable pass: it catches
obvious visual regressions and any main-view corruption with the least effort.
Choppy framerate is expected on the Deck and is not a failure.

Pre:
- [ ] Test stack installed (`live_tests/modtest.md` step 1).
- [ ] `modswap.sh status` shows scatterer/eve/tufx/firefly/deferred/parallax all `present`.
- [ ] TUFX profile loaded (CKAN ships none; pick one in the TUFX UI).
- [ ] kerbcast deployed (current branch DLL + sidecar on the Deck), all integration
      toggles in settings.cfg at their defaults (on).
- [ ] A vessel with at least one Hullcam part, on the pad and able to fly.

Sweep (open the kerbcast web page; watch a feed per layer where relevant):

- [ ] TUFX: post-processing (tonemap/bloom/colour grade) is present on the stream and
      matches the in-game look across near, far terrain, scaled space, and galaxy.
- [ ] EVE: clouds appear on the stream over the planet (near and at scaled-space range).
- [ ] Scatterer: atmosphere/sky/sunflare/ocean appear on the stream as in-game.
- [ ] Parallax: surface scatters (rocks/grass) appear on the stream near the ground.
- [ ] Deferred: surfaces are correctly lit on the stream (no all-black surface).
- [ ] Firefly (in atmosphere, descending fast): reentry plasma appears on the near-cam
      stream; absent in orbit / on the pad.

Hard gates (any failure = not done):
- [ ] **Player MAIN view is unchanged.** Compare the in-game view to a known-good
      session: no flicker, no corrupted sky, no missing terrain, no doubled effects.
      This is the critical safety property (Scatterer's singleton swap and Parallax's
      global-state swap must restore cleanly each frame).
- [ ] No new Kerbcast-tagged exceptions in KSP.log
      (`grep -i "\[Kerbcast" KSP.log | grep -i "exception\|error"`).
- [ ] kerbcast still streams (cameras render; no black feeds).
- [ ] Adaptive shedding keeps KSP physics in the safe band, or the "throttled
      because X" overlay explains why not.

If a layer is wrong or the main view is corrupted, drop to Tier 2 isolation
(`live_tests/modtest.md` step 2) to attribute, then the per-integration card.

Perf notes (informational): record KSP FPS with the full stack, and the /metrics
per-phase timings, so we know the worst-case headroom shedding has to work with.

Result: pass | fail
Notes:
