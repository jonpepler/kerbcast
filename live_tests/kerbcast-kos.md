# Live test: KerbcastKos kOS addon (goal check #3)

Verifies the `ADDONS:KERBCAST` kerboscript surface end to end on a real KSP flight:
enumerate cameras, zoom, pan, and track a target with a recalculating `AIM` source.
This is the one check that needs the Deck (a real KSP + GPU + kerbcast stream); the
suffix logic and a headless `PRINT ADDONS:KERBCAST:...` run are already covered by
`Plugin/PanAim.Tests`, `Plugin/AimSourceRegistry.Tests`, and `Plugin/KerbcastKos.Tests`.

## Prerequisites

- kerbcast installed and streaming (plugin + sidecar), a browser/gonogo watching the feed.
- kOS installed.
- The addon deployed: build + copy `Kerbcast.Kos.dll` into the KSP install, then
  **restart KSP** (kOS addons load at startup):

  ```sh
  M=~/personal/gonogo/local_docs/syncthing/kspdata
  dotnet build Plugin/KerbcastKos/KerbcastKos.csproj -c Release \
    /p:KspManaged="$M/KSP_Data/Managed" /p:KspGameData="$M/GameData"
  mkdir -p "$M/GameData/KerbcastKos/Plugins"
  cp Plugin/KerbcastKos/bin/Release/Kerbcast.Kos.dll "$M/GameData/KerbcastKos/Plugins/"
  ```
  (Syncthing carries it to the Deck; the plugin `Kerbcast.dll` it links must already be in
  `GameData/Kerbcast/Plugins/`.)

- A vessel with **a `DC.TurretCam`** (steerable, yaw ±135) and at least one zoom-capable
  Hullcam, plus a **kOS CPU** aboard. Optionally a `hc.launchcam` to see pitch (TurretCam is
  yaw-only, so its `PANPITCH` clamps to 0). Set a target (another vessel/body) to exercise `AIM`.

## Script (`kerbcast-kos-test.ks` on the CPU's volume)

```
PRINT "kerbcast available: " + ADDONS:KERBCAST:AVAILABLE.
PRINT "cameras: " + ADDONS:KERBCAST:CAMERAS:LENGTH.
FOR c IN ADDONS:KERBCAST:CAMERAS {
  PRINT "- " + c:NAME + " uid=" + c:UID
      + " zoom=" + c:SUPPORTSZOOM + " pan=" + c:SUPPORTSPAN
      + " fov=" + ROUND(c:FOV,1) + " [" + ROUND(c:FOVMIN,0) + ".." + ROUND(c:FOVMAX,0) + "]".
}

// Zoom: narrow the first zoom-capable camera toward its tight end.
FOR c IN ADDONS:KERBCAST:CAMERAS {
  IF c:SUPPORTSZOOM {
    SET c:FOV TO c:FOVMIN + 5.
    PRINT c:NAME + " fov -> " + ROUND(c:FOV,1).
    BREAK.
  }
}

// Pan: sweep the turret, then continuously track the target.
FOR c IN ADDONS:KERBCAST:CAMERAS {
  IF c:SUPPORTSPAN {
    PRINT "panning " + c:NAME.
    SET c:PANYAW TO 45.   WAIT 2.   // yaw right
    SET c:PANYAW TO -45.  WAIT 2.   // yaw left
    SET c:PANPITCH TO 15. WAIT 2.   // no-op on yaw-only DC.TurretCam; visible on hc.launchcam
    IF HASTARGET {
      PRINT "tracking " + TARGET:NAME.
      SET c:AIM TO { RETURN TARGET:POSITION. }.  // recalculating source, re-evaluated each tick
      WAIT 8.
      SET c:AIM TO 0.                            // clear the source; camera holds last angle
      PRINT "tracking stopped".
    }
    BREAK.
  }
}
PRINT "done".
```

Run: `RUN kerbcast-kos-test.` (or `RUNPATH`).

## Expected observations

Terminal:
- `kerbcast available: True`.
- `cameras:` equals the vessel's Hullcam count; each line shows plausible flags (the
  `DC.TurretCam` line has `pan=True`; a NavCam-type line has `zoom=True`) and an FOV range.

On the kerbcast stream (browser/gonogo):
- The zoom-capable camera's view **narrows smoothly** to near `FOVMIN` (eased by the plugin's
  slew, not a hard cut).
- The turret camera's view **sweeps right (+45 yaw), then left (-45)**, smoothly. The `PANPITCH`
  line does nothing on `DC.TurretCam` (yaw-only); on `hc.launchcam` the view tilts up ~15.
- With a target set, the turret **tracks the target continuously** as the vessel/target move
  (this is the `AIM` source re-evaluating each tick). After `SET c:AIM TO 0`, the camera **holds**
  its last angle instead of following.
- Moves are immediate (craft-local, no operator signal delay).

## Pass criteria

`AVAILABLE` is true, `CAMERAS` enumerates the vessel's cameras, FOV and yaw changes are
visible on the stream, and the `AIM` source visibly tracks then stops on clear. Note any suffix
that misbehaves (wrong direction, no clamp, exception in the terminal) back to the addon issue.
