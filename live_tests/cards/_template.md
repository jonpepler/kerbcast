# <Mod> integration test card

Template for a per-integration Tier-1/Tier-2 card. Copy to `<mod>.md`, fill the
angle-bracket placeholders, delete this line.

Pre:
- [ ] Test stack installed (`live_tests/modtest.md` step 1); `<Mod>` present
      (`modswap.sh status` shows it `present`).
- [ ] kerbcast deployed (current branch DLL + sidecar on the Deck).
- [ ] Settings for this run: `<settings.cfg key(s)>` = `<value(s)>`.

Tier 1 (mod on, full stack, choppy is fine):
- [ ] Effect appears on the correct stream layer(s): <which layers, what to look for>.
- [ ] Player MAIN view is unchanged vs before kerbcast was running (the safety gate;
      especially for mods that touch global/singleton state).
- [ ] No exceptions in KSP.log: `grep -i "exception\|\[Kerbcast" KSP.log` shows no new
      Kerbcast-tagged errors.
- [ ] Expected log line(s): <e.g. "[Kerbcast-<mod>] integration enabled">.
- [ ] Shedding/throttle behaves under load (note KSP FPS; any "throttled because X").

Tier 2 (isolation, only if Tier 1 fails):
- [ ] Toggle `<Mod>` capture off in settings.cfg, re-enter flight scene, recheck the
      failing item (attributes it to kerbcast's capture vs the mod itself).
- [ ] `modswap.sh disable <mod>`, restart, capture the mod-absent baseline.

Perf notes (informational, never blocks):
- /metrics per-phase delta, capture-on vs capture-off, on the affected layer(s):
  <record here>.

Result: pass | fail
Notes:
