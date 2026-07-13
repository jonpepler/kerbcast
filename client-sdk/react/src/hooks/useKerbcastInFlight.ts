import { useEffect, useState } from "react";
import { useKerbcastClient } from "../context";

/** Whether KSP is in a flight scene. `undefined` until the first signal. */
export function useKerbcastInFlight(): boolean | undefined {
  const client = useKerbcastClient();
  const [inFlight, setInFlight] = useState(() => client.inFlight);

  useEffect(() => {
    setInFlight(client.inFlight);
    return client.on("scene-change", () => setInFlight(client.inFlight));
  }, [client]);

  return inFlight;
}
