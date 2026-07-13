/**
 * Shared standby glyph: a rocket on the pad ("waiting for a flight"). Used by
 * the CameraFeed out-of-flight overlay and the web dashboard standby so the
 * two read as one system. Inherits `currentColor`.
 */
export function StandbyIcon({ size = 32 }: { size?: number }) {
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
      {/* body + nose cone */}
      <path d="M12 2.5c1.7 1.9 2.6 4.5 2.6 7.3V16H9.4V9.8C9.4 7 10.3 4.4 12 2.5Z" />
      {/* window */}
      <circle cx="12" cy="8.8" r="1.3" />
      {/* fins */}
      <path d="M9.4 13 7.2 16.8 9.4 15.8Z" />
      <path d="M14.6 13 16.8 16.8 14.6 15.8Z" />
      {/* launch-clamp legs to the pad */}
      <path d="M10 16 9 20.3" />
      <path d="M14 16 15 20.3" />
      {/* pad ground line */}
      <path d="M4.5 20.5h15" />
    </svg>
  );
}
