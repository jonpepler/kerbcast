import { beforeEach, describe, expect, it } from "vitest";
import { loadLabel, saveLabel } from "./labels";
import { cameraKey } from "./tiles";

const KEY = "Kerbal X|hull.cam|FwdCam";

describe("custom labels", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("returns null when no label is set (fall back to auto name)", () => {
    expect(loadLabel(KEY)).toBeNull();
  });

  it("sets and reads a custom label", () => {
    saveLabel(KEY, "Booster Nose");
    expect(loadLabel(KEY)).toBe("Booster Nose");
  });

  it("trims whitespace on save", () => {
    saveLabel(KEY, "  Padded  ");
    expect(loadLabel(KEY)).toBe("Padded");
  });

  it("clearing with an empty string removes the label (falls back to auto)", () => {
    saveLabel(KEY, "Something");
    expect(loadLabel(KEY)).toBe("Something");
    expect(saveLabel(KEY, "")).toBeNull();
    expect(loadLabel(KEY)).toBeNull();
  });

  it("clearing with whitespace-only removes the label", () => {
    saveLabel(KEY, "Something");
    saveLabel(KEY, "   ");
    expect(loadLabel(KEY)).toBeNull();
  });

  it("saveLabel returns the stored (trimmed) value, or null when cleared", () => {
    expect(saveLabel(KEY, "  Aft  ")).toBe("Aft");
    expect(saveLabel(KEY, "")).toBeNull();
  });

  it("keys labels independently: one camera's label does not leak to another", () => {
    const other = "Kerbal X|hull.cam|AftCam";
    saveLabel(KEY, "Front");
    saveLabel(other, "Rear");
    expect(loadLabel(KEY)).toBe("Front");
    expect(loadLabel(other)).toBe("Rear");
  });

  it("ignores a null/undefined/empty key (no crash, no storage)", () => {
    expect(saveLabel(null, "x")).toBeNull();
    expect(saveLabel(undefined, "x")).toBeNull();
    expect(saveLabel("", "x")).toBeNull();
    expect(loadLabel(null)).toBeNull();
    expect(loadLabel(undefined)).toBeNull();
    expect(loadLabel("")).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// stable-identity keying: labels survive KSP revert/recover (flightId change)
// ---------------------------------------------------------------------------

describe("labels key off stable identity, not flightId", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("a label set before a revert is found again under the new flightId", () => {
    // Same physical camera, different flightId before and after a revert.
    const before = { flightId: 100, vesselName: "Kerbal X", partName: "hull.cam", cameraName: "FwdCam" };
    const after = { flightId: 777, vesselName: "Kerbal X", partName: "hull.cam", cameraName: "FwdCam" };

    // Store keyed by stable identity of the pre-revert camera.
    saveLabel(cameraKey(before), "Nose Cam");

    // After revert the flightId changed, but the stable key is identical, so
    // the label resolves.
    expect(cameraKey(after)).toBe(cameraKey(before));
    expect(loadLabel(cameraKey(after))).toBe("Nose Cam");
  });

  it("a different physical camera does not inherit the label", () => {
    const a = { flightId: 1, vesselName: "Kerbal X", partName: "hull.cam", cameraName: "FwdCam" };
    const b = { flightId: 2, vesselName: "Kerbal X", partName: "hull.cam", cameraName: "AftCam" };
    saveLabel(cameraKey(a), "Nose Cam");
    expect(loadLabel(cameraKey(b))).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// persistence: survives reload (fresh module read of localStorage)
// ---------------------------------------------------------------------------

describe("persistence across reload", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("reads back a label written in a previous 'session' (raw localStorage)", () => {
    // Simulate a value written before a reload.
    localStorage.setItem("kerbcast:labels", JSON.stringify({ [KEY]: "Persisted" }));
    expect(loadLabel(KEY)).toBe("Persisted");
  });

  it("tolerates a corrupt store (non-object) by falling back to auto name", () => {
    localStorage.setItem("kerbcast:labels", "not json");
    expect(loadLabel(KEY)).toBeNull();
    localStorage.setItem("kerbcast:labels", JSON.stringify([1, 2, 3]));
    expect(loadLabel(KEY)).toBeNull();
  });
});
