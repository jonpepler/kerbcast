# Firefly reentry-capture test card

Pre:
- [ ] Test stack installed; Firefly present (`modswap.sh status` shows `firefly`).
- [ ] kerbcast deployed (current branch DLL + sidecar on the Deck).
- [ ] Settings: `EnableFirefly = true`, `EnableAtmosphericFx = true`.

Tier 1 (Firefly present, descending fast in atmosphere):
- [ ] Firefly reentry plasma (sheath/bowshock/glow) appears on the near-cam stream
      during reentry, matching the player's Firefly look.
- [ ] kerbcast's OWN reentry FX (Core/Bowshock/Trail/Embers) do NOT also appear:
      no doubled plasma. The provider substitution made them mutually exclusive.
- [ ] Firefly's particle sparks also appear on the stream (these come free via the
      cullingMask copy; no extra handling).
- [ ] In orbit / on the pad: no reentry plasma on the stream (Firefly inactive, the
      effect removes its borrowed buffers).
- [ ] Player MAIN view unchanged.
- [ ] No new Kerbcast exceptions: `grep -i "\[Kerbcast-Firefly\]\|exception" KSP.log`
      shows `[Kerbcast-Firefly] capture effect enabled` and no attach/detach errors.

Provider-selection checks:
- [ ] Set `EnableFirefly = false`, re-enter flight scene, reenter atmosphere:
      kerbcast's OWN reentry FX (Core/Bowshock/Trail/Embers) appear instead. This
      confirms the default-vs-Firefly substitution flips correctly.
- [ ] `modswap.sh disable firefly`, restart: kerbcast's own reentry FX appear
      (Firefly-absent baseline; the substitution falls back automatically).

Perf notes: /metrics near-phase delta with Firefly capture vs kerbcast's own FX.

Result: pass | fail
Notes:
