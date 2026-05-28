const NOISE_MAX_W = 320;
const NOISE_MAX_H = 180;

export interface NoisePipeline {
  readonly processedStream: MediaStream;
  setIntensity(level: number): void;
  destroy(): void;
}

/**
 * Wraps a raw `MediaStream` in a canvas pipeline that composites
 * digital static over the video. Returns null when `captureStream` is
 * unavailable (SSR, test environments without canvas support).
 */
export function tryCreateNoisePipeline(
  rawStream: MediaStream,
  initialIntensity: number,
): NoisePipeline | null {
  const canvas = document.createElement("canvas");
  if (typeof (canvas as HTMLCanvasElement & { captureStream?: unknown }).captureStream !== "function") {
    return null;
  }

  const noiseCanvas = document.createElement("canvas");
  const noiseCtx = noiseCanvas.getContext("2d");
  const ctx = canvas.getContext("2d");
  if (!noiseCtx || !ctx) return null;

  // Size the output canvas to video dimensions once metadata loads.
  // Until then, use a placeholder so captureStream has something to output.
  canvas.width = NOISE_MAX_W;
  canvas.height = NOISE_MAX_H;
  noiseCanvas.width = Math.ceil(NOISE_MAX_W / 2);
  noiseCanvas.height = Math.ceil(NOISE_MAX_H / 2);

  let intensity = initialIntensity;
  let rafId: number | null = null;
  let destroyed = false;

  const video = document.createElement("video");
  video.srcObject = rawStream;
  video.muted = true;
  video.playsInline = true;

  const onMeta = () => {
    const vw = video.videoWidth;
    const vh = video.videoHeight;
    if (!vw || !vh) return;
    canvas.width = vw;
    canvas.height = vh;
    const scale = Math.min(1, NOISE_MAX_W / vw, NOISE_MAX_H / vh);
    noiseCanvas.width = Math.max(1, Math.round(vw * scale));
    noiseCanvas.height = Math.max(1, Math.round(vh * scale));
  };
  video.addEventListener("loadedmetadata", onMeta);
  void video.play();

  const draw = () => {
    if (destroyed) return;
    const cw = canvas.width;
    const ch = canvas.height;
    const nw = noiseCanvas.width;
    const nh = noiseCanvas.height;

    if (video.readyState >= 2) {
      ctx.drawImage(video, 0, 0, cw, ch);
    }

    // Build noise at reduced resolution; composited up to full canvas size.
    const imageData = noiseCtx.createImageData(nw, nh);
    const d = imageData.data;
    const dropThreshold = (intensity - 0.45) * 0.35;
    const dropAlpha = Math.round(Math.min(intensity * 1.8, 1) * 230);
    const speckleAlpha = Math.round(intensity * 210);

    for (let row = 0; row < nh; row++) {
      const dropped = intensity > 0.45 && Math.random() < dropThreshold;
      for (let col = 0; col < nw; col++) {
        const i = (row * nw + col) * 4;
        if (dropped) {
          d[i] = d[i + 1] = d[i + 2] = 0;
          d[i + 3] = dropAlpha;
        } else if (Math.random() < intensity * 0.45) {
          const v = Math.floor(Math.random() * 155 + 100);
          d[i] = d[i + 1] = d[i + 2] = v;
          d[i + 3] = speckleAlpha;
        }
        // else: fully transparent (ImageData is zeroed on creation)
      }
    }
    noiseCtx.putImageData(imageData, 0, 0);
    ctx.drawImage(noiseCanvas, 0, 0, cw, ch);

    rafId = requestAnimationFrame(draw);
  };
  rafId = requestAnimationFrame(draw);

  let processedStream: MediaStream;
  try {
    processedStream = (
      canvas as HTMLCanvasElement & { captureStream(fps: number): MediaStream }
    ).captureStream(30);
  } catch {
    destroyed = true;
    if (rafId !== null) cancelAnimationFrame(rafId);
    video.removeEventListener("loadedmetadata", onMeta);
    video.pause();
    video.srcObject = null;
    return null;
  }

  return {
    processedStream,
    setIntensity(level: number) {
      intensity = level;
    },
    destroy() {
      destroyed = true;
      if (rafId !== null) cancelAnimationFrame(rafId);
      video.removeEventListener("loadedmetadata", onMeta);
      video.pause();
      video.srcObject = null;
    },
  };
}
