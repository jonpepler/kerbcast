/**
 * Custom camera labels, backed by localStorage.
 *
 * Each label overrides the auto-generated display name for a single camera.
 * Labels are keyed by the camera's STABLE identity string (the same
 * vesselName|partName|cameraName key used by tiles.ts), not by flightId, so a
 * label survives KSP revert/recover, which reassigns part.flightID.
 *
 * The whole map lives under one localStorage key as a plain object. An empty or
 * whitespace-only label is treated as "unset" and removed, so callers fall back
 * to the auto name.
 */

const STORAGE_KEY = "kerbcast:labels";

type LabelMap = Record<string, string>;

/** Read the full label map. Returns an empty object on any error or absence. */
export function loadLabels(): LabelMap {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw === null) return {};
    const parsed: unknown = JSON.parse(raw);
    if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
      return {};
    }
    const out: LabelMap = {};
    for (const [k, v] of Object.entries(parsed as Record<string, unknown>)) {
      if (typeof v === "string" && v.trim() !== "") out[k] = v;
    }
    return out;
  } catch {
    return {};
  }
}

/** Persist the full label map. */
function saveLabels(labels: LabelMap): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(labels));
  } catch {
    // ignore (private browsing / storage full)
  }
}

/**
 * The custom label for a stable camera key, or null when none is set. An empty
 * key (degenerate camera identity) never carries a label.
 */
export function loadLabel(key: string | null | undefined): string | null {
  if (!key) return null;
  const labels = loadLabels();
  return labels[key] ?? null;
}

/**
 * Set (or clear) the custom label for a stable camera key. A trimmed-empty
 * value clears the label so the caller falls back to the auto name. Returns the
 * trimmed value that was stored, or null when cleared. A missing/empty key is a
 * no-op.
 */
export function saveLabel(key: string | null | undefined, label: string): string | null {
  if (!key) return null;
  const labels = loadLabels();
  const trimmed = label.trim();
  if (trimmed === "") {
    delete labels[key];
    saveLabels(labels);
    return null;
  }
  labels[key] = trimmed;
  saveLabels(labels);
  return trimmed;
}
