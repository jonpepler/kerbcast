/**
 * Shared standby glyph: a hard hat ("ground crew at work between missions,
 * no live feed"). Used by the CameraFeed out-of-flight overlay and the web
 * dashboard standby so the two read as one system. Inherits `currentColor`.
 */
export function HardHatIcon({ size = 32 }: { size?: number }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M2 18h20" />
      <path d="M4 18v-3a8 8 0 0 1 16 0v3" />
      <path d="M9.5 7.5V5.2A1.7 1.7 0 0 1 11.2 3.5h1.6A1.7 1.7 0 0 1 14.5 5.2V7.5" />
    </svg>
  );
}
