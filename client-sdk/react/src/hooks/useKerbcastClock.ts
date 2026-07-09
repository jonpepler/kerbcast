import { useEffect, useState } from "react";
import { useKerbcastClient } from "../context";

/**
 * Mission-time capture clock, from the sidecar's ~1Hz `settings-state`.
 *
 * - `captureUt`: KSP universal time (seconds) the current video was captured
 *   at, or `null` when no clock is known (old plugin/sidecar, or before the
 *   first push). A consumer treats `null` as "no clock" and keeps a live
 *   passthrough.
 * - `epoch`: bumps on a UT discontinuity (revert / quickload / scene reload);
 *   only the change is meaningful, so a consumer flushes and resyncs on it.
 * - `warpRate`: current time-warp multiplier, for interpolating `captureUt`
 *   between samples; defaults to 1.
 *
 * This is a single global sim clock, not a per-camera value, so it takes no
 * `flightId`.
 */
export function useKerbcastClock(): {
  captureUt: number | null;
  epoch: number;
  warpRate: number;
} {
  const client = useKerbcastClient();
  const [clock, setClock] = useState(() => client.clock);

  useEffect(() => {
    setClock(client.clock);
    return client.on("settings-change", () => setClock(client.clock));
  }, [client]);

  return clock;
}
