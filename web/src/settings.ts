/**
 * Persistent settings for the kerbcast web page.
 * Backed by localStorage. Each getter reads the stored value on every call
 * so callers always see the latest.
 */

export type ThemePreference = "auto" | "light" | "dark";

/** Where the crew bar docks: a horizontal filmstrip, a vertical column, or a
 *  reflowing wrap. Squares stay square in all three. */
export type CrewBarPlacement = "row" | "column" | "wrap";

const KEY_THEME = "kerbcast:theme";
const KEY_DEBUG = "kerbcast:debug";
const KEY_SHOW_STATIC = "kerbcast:showStatic";
const KEY_SHOW_PERF_WARNINGS = "kerbcast:showPerfWarnings";
const KEY_CREW_BAR_PLACEMENT = "kerbcast:crewBarPlacement";
const KEY_CREW_BAR_DISSOLVE = "kerbcast:crewBarDissolve";

export function loadTheme(): ThemePreference {
  const raw = localStorage.getItem(KEY_THEME);
  if (raw === "light" || raw === "dark" || raw === "auto") return raw;
  return "auto";
}

export function saveTheme(theme: ThemePreference): void {
  localStorage.setItem(KEY_THEME, theme);
}

/** Apply data-theme to <html>. Call whenever theme changes. */
export function applyTheme(theme: ThemePreference): void {
  if (theme === "auto") {
    document.documentElement.removeAttribute("data-theme");
  } else {
    document.documentElement.setAttribute("data-theme", theme);
  }
}

export function loadDebug(): boolean {
  return localStorage.getItem(KEY_DEBUG) === "true";
}

export function saveDebug(enabled: boolean): void {
  localStorage.setItem(KEY_DEBUG, String(enabled));
}

/**
 * Show-static preference. Returns `null` when no explicit value has been
 * stored (auto mode: resolved from `prefers-reduced-motion` at the call
 * site), `true`/`false` for an explicit override.
 */
export function loadShowStatic(): boolean | null {
  const raw = localStorage.getItem(KEY_SHOW_STATIC);
  if (raw === null) return null;
  return raw !== "false";
}

export function saveShowStatic(enabled: boolean): void {
  localStorage.setItem(KEY_SHOW_STATIC, String(enabled));
}

/**
 * Show-performance-warnings preference. Returns `true` when the key is absent
 * (default on: new users see throttle warnings). Returns `false` only when
 * explicitly stored as "false".
 */
export function loadShowPerfWarnings(): boolean {
  return localStorage.getItem(KEY_SHOW_PERF_WARNINGS) !== "false";
}

export function saveShowPerfWarnings(enabled: boolean): void {
  localStorage.setItem(KEY_SHOW_PERF_WARNINGS, String(enabled));
}

/** Crew-bar placement. Defaults to a bottom row when unset. */
export function loadCrewBarPlacement(): CrewBarPlacement {
  const raw = localStorage.getItem(KEY_CREW_BAR_PLACEMENT);
  if (raw === "row" || raw === "column" || raw === "wrap") return raw;
  return "row";
}

export function saveCrewBarPlacement(placement: CrewBarPlacement): void {
  localStorage.setItem(KEY_CREW_BAR_PLACEMENT, placement);
}

/**
 * Dissolve preference. When true the crew bar is dissolved: kerbal face cams
 * render inline in the main content area instead of a docked bar. Defaults to
 * false (docked bar) when unset.
 */
export function loadCrewBarDissolve(): boolean {
  return localStorage.getItem(KEY_CREW_BAR_DISSOLVE) === "true";
}

export function saveCrewBarDissolve(dissolve: boolean): void {
  localStorage.setItem(KEY_CREW_BAR_DISSOLVE, String(dissolve));
}
