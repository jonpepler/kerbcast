/**
 * useKerbcastInFlight scene-state subscription.
 *
 * Renders a probe through the real hook + real KerbcastClient with only the
 * WebRTC transport faked by the SDK's MockSidecar. Asserts the hook starts
 * undefined (no signal yet) and re-renders on each `scene-state-changed`.
 */

import { KerbcastClient } from "@ksp-gonogo/kerbcast";
import { MockSidecar } from "@ksp-gonogo/kerbcast/testing";
import { act, cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { KerbcastProvider } from "../context";
import { useKerbcastInFlight } from "./useKerbcastInFlight";

let last: boolean | undefined;

function Probe(): null {
  last = useKerbcastInFlight();
  return null;
}

async function connected(): Promise<{
  client: KerbcastClient;
  sidecar: MockSidecar;
}> {
  const sidecar = new MockSidecar();
  const client = new KerbcastClient(
    { host: "h", port: 1, negotiate: (o) => sidecar.negotiate(o) },
    sidecar.createTransport(),
  );
  await act(async () => {
    await client.connect([], { slots: 4 });
    sidecar.open();
    sidecar.setConnectionState("connected");
  });
  return { client, sidecar };
}

afterEach(() => {
  cleanup();
  last = undefined;
});

describe("useKerbcastInFlight", () => {
  it("starts undefined and reflects scene-state-changed", async () => {
    const { client, sidecar } = await connected();

    render(
      <KerbcastProvider client={client}>
        <Probe />
      </KerbcastProvider>,
    );

    expect(last).toBeUndefined();

    await act(async () => {
      sidecar.fireSceneState(false);
    });
    expect(last).toBe(false);

    await act(async () => {
      sidecar.fireSceneState(true);
    });
    expect(last).toBe(true);

    await act(async () => {
      await client.disconnect();
    });
  });
});
