/**
 * CrewBar — the crew face-camera surface.
 *
 * Camera-driven: renders every `kind === "kerbal"` camera as a square
 * KerbalFaceFeed (keyed by flightId), with a name label, an IVA/EVA badge from
 * crewLocation, and a SIGNAL LOST treatment for a destroyed kerbal. Kerbal cams
 * are never part of the part-camera tile grid, so this is their single home.
 *
 * Placement (row / column / wrap) and minimise are CSS-only reflows of the SAME
 * mounted feeds — the "never remount a live feed on re-layout" rule: switching
 * placement or minimising never unmounts a KerbalFaceFeed. `inline` mode (crew
 * bar dissolved) drops the dock chrome and always wraps, so the faces read as
 * part of the main content flow instead of a docked bar.
 */

import { CrewLocation } from "@ksp-gonogo/kerbcast";
import type { CameraState } from "@ksp-gonogo/kerbcast";
import { KerbalFaceFeed, isCameraDestroyed, useKerbcastCameras } from "@ksp-gonogo/kerbcast-react";
import type { FeedAction } from "@ksp-gonogo/kerbcast-react";
import { Maximize2, PanelBottomClose, PanelBottomOpen, X } from "lucide-react";
import { useMemo, useRef, useState } from "react";
import styled from "styled-components";
import type { CrewBarPlacement } from "./settings";

interface CrewBarProps {
  /** Dock layout (ignored when `inline`, which always wraps). */
  placement: CrewBarPlacement;
  /** Dissolved: render inline in the content flow, no dock chrome / minimise. */
  inline?: boolean;
  /** Collapsed out of the way (feeds stay mounted). Docked mode only. */
  minimised?: boolean;
  onToggleMinimise?: () => void;
}

export function CrewBar({
  placement,
  inline = false,
  minimised = false,
  onToggleMinimise,
}: CrewBarProps): React.JSX.Element | null {
  const cameras = useKerbcastCameras();
  const kerbals = useMemo(
    () => cameras.filter((c) => c.kind === "kerbal"),
    [cameras],
  );

  // Dismissed faces (session-only: a user hiding a face this visit).
  const [dismissed, setDismissed] = useState<ReadonlySet<number>>(() => new Set());
  const dismiss = (flightId: number) =>
    setDismissed((prev) => {
      const next = new Set(prev);
      next.add(flightId);
      return next;
    });

  const visible = kerbals.filter((k) => !dismissed.has(k.flightId));

  // Nothing to show: no crew cameras at all (or all dismissed) -> render
  // nothing, so the bar doesn't reserve empty space.
  if (visible.length === 0) return null;

  // Inline (dissolved) always wraps; docked uses the chosen placement.
  const effPlacement: CrewBarPlacement = inline ? "wrap" : placement;

  return (
    <Root $placement={effPlacement} $inline={inline} data-testid="crew-bar" data-placement={effPlacement} data-inline={inline}>
      {!inline && (
        <Bar>
          <BarTitle>Crew</BarTitle>
          {onToggleMinimise && (
            <MinButton
              type="button"
              aria-label={minimised ? "Show crew" : "Hide crew"}
              aria-pressed={minimised}
              onClick={onToggleMinimise}
            >
              {minimised ? <PanelBottomOpen size={15} aria-hidden="true" /> : <PanelBottomClose size={15} aria-hidden="true" />}
            </MinButton>
          )}
        </Bar>
      )}
      {/* Faces stay mounted when minimised — collapsed via CSS, never unmounted. */}
      <Faces $placement={effPlacement} $minimised={!inline && minimised} aria-hidden={!inline && minimised}>
        {visible.map((k) => (
          <CrewFace key={k.flightId} cam={k} onDismiss={() => dismiss(k.flightId)} />
        ))}
      </Faces>
    </Root>
  );
}

// ---------------------------------------------------------------------------
// A single crew face
// ---------------------------------------------------------------------------

function CrewFace({ cam, onDismiss }: { cam: CameraState; onDismiss: () => void }): React.JSX.Element {
  const wrapRef = useRef<HTMLDivElement>(null);
  const destroyed = isCameraDestroyed(cam);
  const eva = cam.crewLocation === CrewLocation.Eva;
  const name = cam.cameraName || "Kerbal";

  // Fullscreen the FACE CONTAINER (the video goes fullscreen with it) — the
  // KerbalFaceFeed primitive doesn't expose its <video>, so element fullscreen
  // on the wrapper is the composable path. Dismiss hides this face this visit.
  const actions = useMemo<FeedAction[]>(
    () => [
      {
        id: "fullscreen",
        label: "Fullscreen",
        icon: <Maximize2 size={13} strokeWidth={2} aria-hidden="true" />,
        onClick: () => toggleFullscreen(wrapRef.current),
      },
      {
        id: "dismiss",
        label: "Dismiss",
        icon: <X size={13} strokeWidth={2} aria-hidden="true" />,
        onClick: onDismiss,
      },
    ],
    [onDismiss],
  );

  return (
    <FaceWrap ref={wrapRef} data-testid="crew-face" data-flight-id={cam.flightId} data-destroyed={destroyed}>
      <KerbalFaceFeed flightId={cam.flightId} actions={actions} showStandby={!destroyed}>
        <Overlay>
          <Badge $eva={eva}>{eva ? "EVA" : "IVA"}</Badge>
          {destroyed && <Lost>SIGNAL LOST</Lost>}
          <Name title={name}>{name}</Name>
        </Overlay>
      </KerbalFaceFeed>
    </FaceWrap>
  );
}

function toggleFullscreen(el: HTMLElement | null): void {
  if (!el) return;
  const d = document as Document & {
    webkitFullscreenElement?: Element | null;
    webkitExitFullscreen?: () => void;
  };
  const e = el as HTMLElement & { webkitRequestFullscreen?: () => void };
  const current = document.fullscreenElement ?? d.webkitFullscreenElement ?? null;
  if (current) {
    (document.exitFullscreen ?? d.webkitExitFullscreen)?.call(document);
  } else {
    (e.requestFullscreen ?? e.webkitRequestFullscreen)?.call(e);
  }
}

// ---------------------------------------------------------------------------
// Styled
// ---------------------------------------------------------------------------

/** Fixed square edge for a crew face (px). */
const FACE = 132;

const Root = styled.div<{ $placement: CrewBarPlacement; $inline: boolean }>`
  display: flex;
  flex-direction: column;
  min-height: 0;
  ${(p) =>
    p.$inline
      ? `
    /* Dissolved: sits in the content flow, full width, no dock chrome. */
    padding: 0.75rem 1rem 0;
  `
      : p.$placement === "column"
        ? `
    /* Side column: fixed-width vertical dock. */
    width: ${FACE + 32}px;
    flex: 0 0 auto;
    border-left: 1px solid var(--kc-border, rgba(255,255,255,0.1));
    background: var(--kc-surface, rgba(0,0,0,0.15));
  `
        : `
    /* Bottom dock (row / wrap): full-width horizontal strip. */
    flex: 0 0 auto;
    border-top: 1px solid var(--kc-border, rgba(255,255,255,0.1));
    background: var(--kc-surface, rgba(0,0,0,0.15));
  `}
`;

const Bar = styled.div`
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.3rem 0.6rem;
  flex: 0 0 auto;
`;

const BarTitle = styled.span`
  font-size: 0.68rem;
  font-weight: 600;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--kc-text-muted, rgba(255,255,255,0.6));
`;

const MinButton = styled.button`
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 0;
  border: none;
  border-radius: 3px;
  cursor: pointer;
  background: transparent;
  color: var(--kc-text-muted, rgba(255,255,255,0.6));

  &:hover {
    color: var(--kc-text, #fff);
    background: rgba(255, 255, 255, 0.08);
  }
  &:focus-visible {
    outline: 2px solid var(--kc-accent, #6ab0ff);
    outline-offset: 2px;
  }
`;

const Faces = styled.div<{ $placement: CrewBarPlacement; $minimised: boolean }>`
  display: flex;
  gap: 0.6rem;
  padding: 0.6rem;
  ${(p) =>
    p.$placement === "column"
      ? `flex-direction: column; overflow-y: auto; align-items: center;`
      : p.$placement === "wrap"
        ? `flex-direction: row; flex-wrap: wrap; overflow-y: auto; align-content: flex-start;`
        : `flex-direction: row; flex-wrap: nowrap; overflow-x: auto;`}

  /* Minimise collapses the strip without unmounting the feeds. */
  ${(p) =>
    p.$minimised
      ? `max-height: 0; padding-top: 0; padding-bottom: 0; overflow: hidden; opacity: 0; pointer-events: none;`
      : ``}
  transition: max-height 0.18s ease, opacity 0.18s ease;

  @media (prefers-reduced-motion: reduce) {
    transition: none;
  }
`;

const FaceWrap = styled.div`
  position: relative;
  width: ${FACE}px;
  height: ${FACE}px;
  flex: 0 0 auto;

  /* Actions (top-right, from the primitive) hover-reveal: hidden until the face
     is hovered or a control is focused (keyboard reachable). */
  & button {
    opacity: 0;
    transition: opacity 0.12s ease;
  }
  &:hover button,
  &:focus-within button {
    opacity: 1;
  }
  @media (prefers-reduced-motion: reduce) {
    & button {
      transition: none;
    }
  }
`;

const Overlay = styled.div`
  position: absolute;
  inset: 0;
  pointer-events: none;
`;

const Badge = styled.span<{ $eva: boolean }>`
  position: absolute;
  top: 4px;
  left: 4px;
  padding: 1px 5px;
  font-size: 0.6rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  border-radius: 3px;
  color: #fff;
  background: ${(p) => (p.$eva ? "rgba(230,140,30,0.85)" : "rgba(60,120,200,0.85)")};
`;

const Name = styled.span`
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  padding: 3px 6px;
  font-size: 0.68rem;
  font-weight: 600;
  color: #fff;
  background: linear-gradient(to top, rgba(0, 0, 0, 0.7), transparent);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
`;

const Lost = styled.span`
  position: absolute;
  top: 50%;
  left: 0;
  right: 0;
  transform: translateY(-50%);
  text-align: center;
  font-size: 0.62rem;
  font-weight: 700;
  letter-spacing: 0.14em;
  color: #ff6b6b;
`;
