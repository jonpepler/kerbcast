# Deferred integration test card

Pre:
- [ ] Test stack installed; Deferred present (`modswap.sh status` shows `deferred`).
- [ ] kerbcast deployed (current branch DLL + sidecar on the Deck).
- [ ] Settings: `EnableDeferred = true`.

Tier 1 (mod on, full stack, choppy is fine):
- [ ] Near-cam and far-cam streams render terrain/parts CORRECTLY LIT (not black).
      Without this integration a deferred clone renders forward-authored materials
      black; the fix is confirmed by a correctly-lit surface on the stream.
- [ ] The deferred look on the stream matches the player's in-game deferred look.
- [ ] Scaled-cam stream still renders (it stays on kerbcast's forced Forward path;
      on Linux/Mesa this is intentional). Note if the scaled surface looks wrong on
      this platform.
- [ ] Player MAIN view unchanged.
- [ ] No new Kerbcast exceptions: `grep -i "\[Kerbcast-Deferred\]\|exception" KSP.log`
      shows `[Kerbcast-Deferred] integration enabled` and no apply/init errors.

Tier 2 (isolation, only if Tier 1 fails):
- [ ] Set `EnableDeferred = false`, re-enter flight scene: near/far clones likely go
      black (confirming the integration was what fixed them), or fall back to forward
      depending on path. This is the attribution check.
- [ ] `modswap.sh disable deferred`, restart: clean Deferred-absent baseline (clones
      are forward, render normally).

Platform note: the scaled clone is left on Forward to dodge the Mesa/OpenGL deferred-RT
black-surface bug (tier-1 Linux). On Windows/macOS the scaled cam could potentially run
deferred; that is a deliberate future per-platform decision, not done here.

Perf notes: /metrics per-phase deltas with EnableDeferred on vs off.

Result: pass | fail
Notes:
