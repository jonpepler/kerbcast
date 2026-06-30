# Visual-mod integration testing (Deck)

How to test kerbcast's visual-mod integrations on the Steam Deck. The goal is
correctness for other people's installs, not performance on this Deck: kerbcast's
adaptive shedding handles end-user performance, so choppiness here is expected and
fine. See the collected goal at
`docs/superpowers/specs/2026-06-30-visual-mod-delivery-and-testing-design.md`.

## 1. Provision the stack once (CKAN)

Install the whole validated visual stack in one command, deps resolved by CKAN:

```
ckan install --ckanfile live_tests/kerbcast-test-stack.ckan
```

This pulls Scatterer (+ config + sunflare), EVE via Spectra (the cloud config that
pulls the EVE engine), TUFX, Firefly (+ FireflyAPI), Deferred, and Parallax
Continued (+ Kopernicus + texture packs). Notes:

- **TUFX ships no profile via CKAN.** Load a profile in the in-game TUFX UI or drop
  a profile config, or there is nothing for the post-process stack to show.
- **Parallax Continued pulls Kopernicus** (a solar-system loader). That is expected;
  it is what real Parallax users run.
- This ckan file ships no mod files. CKAN fetches official copies, so the test stack
  stays licence-clean.

## 2. Isolate without churn

Two levers, in order of preference:

1. **kerbcast per-integration toggle (no restart).** Each integration has a key in
   `GameData/Kerbcast/PluginData/settings.cfg`; these are re-read on flight-scene
   entry, so flip a key and go KSC -> flight to A/B capture on vs off with the mod
   still installed. This is the primary isolation lever. (Keys are listed in each
   integration's test card as the integration lands.)
2. **modswap.sh (clean mod-absent baseline, needs a KSP restart).** Moves a mod's
   GameData folder(s) into a sibling staging dir so KSP does not load it:

   ```
   KSP_GAMEDATA="/path/to/GameData" live_tests/modswap.sh status
   KSP_GAMEDATA="/path/to/GameData" live_tests/modswap.sh disable scatterer
   KSP_GAMEDATA="/path/to/GameData" live_tests/modswap.sh enable  scatterer
   ```

   Mod keys: `scatterer eve tufx firefly deferred parallax`. Restart KSP after a
   swap. `disable-all` / `enable-all` toggle the whole set.

## 3. Measure (existing telemetry, no new tooling)

- **Per-layer render cost:** kerbcast's `/metrics` reports per-phase timings
  (Galaxy / Scaled / Far / Near / Blit / Readback). The delta with an integration's
  capture toggled on vs off is its marginal cost on that layer.
- **KSP physics FPS** and the in-game "throttled because X" overlay show whether
  adaptive shedding is keeping physics in the safe band.
- See `live_tests/kerbcast.md` for the HTTP endpoints and control-channel shapes.

## 4. Run the cards

Per-integration and full-stack test cards live in `live_tests/cards/`. The
acceptance ladder (from the goal doc):

- **Tier 0 (automated):** plugin builds 0 warnings, Unity-free tests pass, mod-absent
  is a silent no-op. CI covers this.
- **Tier 1 (full-stack interactive, primary):** everything installed, all on; each
  effect appears on the right stream layer; the player's MAIN view is unchanged; no
  exceptions; shedding behaves. Run `cards/full-stack.md`.
- **Tier 2 (isolation, only when Tier 1 flags something):** use the levers in section 2
  to attribute, then the per-integration card.
