# Parallax (ParallaxContinued) integration test card

Pre:
- [ ] Parallax restored from the parked dir (ParallaxContinued + Kopernicus +
      textures). OOM RISK on the 14GB Deck: load incrementally, do NOT stack it on
      the full visual set.
- [ ] Reduced mod set for memory: keep **Parallax + Deferred + Kopernicus**
      (Parallax recommends Deferred for scatter perf); disable the rest -
      `modswap.sh disable eve`, `modswap.sh disable scatterer`,
      `modswap.sh disable tufx`, `modswap.sh disable firefly`. Confirm with
      `modswap.sh status` (parallax + deferred present, others disabled).
- [ ] On a body with a scatter config (Kerbin).
- [ ] kerbcast deployed (current branch DLL + sidecar on the Deck).
- [ ] Settings: `EnableParallax = true`.

Tier 1 (mod on, full stack, choppy is fine):
- [ ] Land or fly low over Kerbin terrain with scatters; surface scatters (rocks,
      grass) appear on the near-cam stream, roughly matching what the player sees.
- [ ] Scatters cull correctly for the camera's view (not drawn behind it / clipped
      wrongly), confirming the per-clone frustum swap works.
- [ ] **HARD GATE: player MAIN view unchanged.** A missed global-state restore would
      mis-position the player's own scatters. Watch the in-game view: scatters must
      not flicker, jump, or disappear while kerbcast streams. NOTE: the drive restores
      Parallax's camera globals but not its GPU append-buffer contents; Parallax
      re-fills those per frame for the main camera, so this should be safe, but if the
      main view's scatters flicker/jump that is the buffer-state vector to fix.
- [ ] No new Kerbcast exceptions: `grep -i "\[Kerbcast-Parallax\]\|exception" KSP.log`
      shows `[Kerbcast-Parallax] integration enabled` and no drive/restore errors.
- [ ] In orbit / off a scatter body: the integration is a cheap no-op (the gate
      skips the drive); confirm no per-frame errors and acceptable FPS.

Tier 2 (isolation, only if Tier 1 fails):
- [ ] Set `EnableParallax = false`, re-enter flight scene: scatters drop from the
      stream; main view unaffected.
- [ ] `modswap.sh disable parallax`, restart: clean Parallax-absent baseline.

Notes:
- This targets ParallaxContinued (Parallax.RuntimeOperations). The archived original
  Parallax/Tessellation is intentionally not captured (clean no-op).
- ParallaxContinued ships no LICENSE file; kerbcast captures by reflection and ships
  nothing of Parallax. If this is to be promoted beyond personal use, confirm the
  license with the maintainer.

Perf notes: this is the heaviest integration (extra GPU evaluate + draw per frame).
Record /metrics near-phase delta and KSP FPS with EnableParallax on vs off; confirm
adaptive shedding keeps physics in the safe band.

Result: pass | fail
Notes:
