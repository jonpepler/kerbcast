/**
 * Tests for the CrewBar: it renders the kerbal face cams (not part cams), with
 * IVA/EVA badges and a SIGNAL LOST treatment for a destroyed kerbal, and it
 * reflows across placement modes + the dissolved (inline) variant.
 *
 * Fixture mirrors Tile.test.tsx / App.test.tsx: a real KerbcastClient +
 * MockSidecar wired through KerbcastProvider so useKerbcastCameras() returns
 * the live list.
 */

import { CameraKind, CrewLocation, KerbcastClient } from "@ksp-gonogo/kerbcast";
import type { CameraLifecycle } from "@ksp-gonogo/kerbcast";
import type { MockCameraInit } from "@ksp-gonogo/kerbcast/testing";
import { MockSidecar } from "@ksp-gonogo/kerbcast/testing";
import { KerbcastProvider } from "@ksp-gonogo/kerbcast-react";
import { act, cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CrewBar } from "./CrewBar";
import type { CrewBarPlacement } from "./settings";

const createdClients: KerbcastClient[] = [];

async function buildConnectedFixture(cameras: MockCameraInit[]) {
  const sidecar = new MockSidecar();
  sidecar.withSlots(["0", "1", "2", "3", "4", "5", "6", "7"]);
  for (const cam of cameras) sidecar.addCamera(cam);

  const client = new KerbcastClient(
    { host: "h", port: 1, negotiate: (o) => sidecar.negotiate(o) },
    sidecar.createTransport(),
  );
  createdClients.push(client);

  await act(async () => {
    await client.connect([], { slots: 8 });
  });
  await act(async () => {
    sidecar.open();
    sidecar.setConnectionState("connected");
  });

  return { client, sidecar };
}

function renderCrewBar(
  client: KerbcastClient,
  props: { placement?: CrewBarPlacement; inline?: boolean; minimised?: boolean } = {},
) {
  return render(
    <KerbcastProvider client={client}>
      <CrewBar placement={props.placement ?? "row"} inline={props.inline} minimised={props.minimised} />
    </KerbcastProvider>,
  );
}

// A representative roster: one part cam (must NOT appear in the bar), three
// seated kerbals, one on EVA, one destroyed.
const ROSTER: MockCameraInit[] = [
  { flightId: 101, cameraName: "NavCam", partName: "mumech.MuMechModuleHullCamera" },
  { flightId: 201, kind: CameraKind.Kerbal, crewLocation: CrewLocation.Seat, cameraName: "Jebediah Kerman" },
  { flightId: 202, kind: CameraKind.Kerbal, crewLocation: CrewLocation.Eva, cameraName: "Valentina Kerman" },
  {
    flightId: 203,
    kind: CameraKind.Kerbal,
    crewLocation: CrewLocation.Seat,
    cameraName: "Kirrim Kerman",
    lifecycle: "destroyed" as CameraLifecycle,
  },
];

beforeEach(() => {
  vi.restoreAllMocks();
});

afterEach(() => {
  cleanup();
  for (const c of createdClients) {
    try { c.disconnect(); } catch { /* ignore */ }
  }
  createdClients.length = 0;
  vi.restoreAllMocks();
});

describe("CrewBar", () => {
  it("renders the kerbal faces (not part cams), with IVA/EVA badges and SIGNAL LOST", async () => {
    const { client } = await buildConnectedFixture(ROSTER);

    let container!: HTMLElement;
    await act(async () => {
      ({ container } = renderCrewBar(client));
    });

    // Each kerbal is present; the part cam is not.
    expect(screen.getByText("Jebediah Kerman")).toBeTruthy();
    expect(screen.getByText("Valentina Kerman")).toBeTruthy();
    expect(screen.getByText("Kirrim Kerman")).toBeTruthy();
    expect(screen.queryByText("NavCam")).toBeNull();

    // One face per kerbal, keyed by flightId.
    const faces = container.querySelectorAll('[data-testid="crew-face"]');
    expect(faces.length).toBe(3);

    // Badges: two IVA (seated Jeb + Kirrim), one EVA (Val).
    expect(screen.getAllByText("IVA").length).toBe(2);
    expect(screen.getAllByText("EVA").length).toBe(1);

    // Destroyed kerbal shows SIGNAL LOST.
    expect(screen.getByText(/signal lost/i)).toBeTruthy();
    const destroyed = container.querySelector('[data-flight-id="203"]');
    expect(destroyed?.getAttribute("data-destroyed")).toBe("true");
  });

  it("reflows across placement modes without changing the roster", async () => {
    const { client } = await buildConnectedFixture(ROSTER);

    for (const placement of ["row", "column", "wrap"] as CrewBarPlacement[]) {
      let container!: HTMLElement;
      await act(async () => {
        ({ container } = renderCrewBar(client, { placement }));
      });
      const bar = container.querySelector('[data-testid="crew-bar"]');
      expect(bar?.getAttribute("data-placement")).toBe(placement);
      expect(container.querySelectorAll('[data-testid="crew-face"]').length).toBe(3);
      cleanup();
    }
  });

  it("dissolved (inline) renders the same faces as a wrap flow, no dock chrome", async () => {
    const { client } = await buildConnectedFixture(ROSTER);

    let container!: HTMLElement;
    await act(async () => {
      ({ container } = renderCrewBar(client, { inline: true }));
    });

    const bar = container.querySelector('[data-testid="crew-bar"]');
    expect(bar?.getAttribute("data-inline")).toBe("true");
    // Inline forces a wrap flow regardless of the placement prop.
    expect(bar?.getAttribute("data-placement")).toBe("wrap");
    // Same crew content.
    expect(container.querySelectorAll('[data-testid="crew-face"]').length).toBe(3);
    // No docked-bar minimise control in inline mode.
    expect(screen.queryByRole("button", { name: /crew/i })).toBeNull();
  });

  it("renders nothing when there are no kerbal cams", async () => {
    const { client } = await buildConnectedFixture([
      { flightId: 101, cameraName: "NavCam", partName: "mumech.MuMechModuleHullCamera" },
    ]);

    let container!: HTMLElement;
    await act(async () => {
      ({ container } = renderCrewBar(client));
    });
    expect(container.querySelector('[data-testid="crew-bar"]')).toBeNull();
  });
});
