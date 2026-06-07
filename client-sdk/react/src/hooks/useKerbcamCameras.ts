import type { CameraState } from "@jonpepler/kerbcam";
import { useEffect, useState } from "react";
import { useKerbcamClient } from "../context";

/**
 * Live snapshot of the kerbcam camera registry. Returns the empty list
 * before the data channel handshake completes (and after a disconnect).
 * Subscribes via the underlying `KerbcamClient`'s `cameras-change` event for
 * one synchronous push per server-side snapshot or state-changed message.
 */
export function useKerbcamCameras(): CameraState[] {
  const client = useKerbcamClient();

  const [cameras, setCameras] = useState<CameraState[]>(() => [
    ...client.cameras,
  ]);

  useEffect(() => {
    setCameras([...client.cameras]);
    return client.on("cameras-change", (next) => setCameras([...next]));
  }, [client]);

  return cameras;
}
