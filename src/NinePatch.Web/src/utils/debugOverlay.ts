import type { DebugLineCandidates } from '../wasm/types';

/**
 * Create a canvas overlay renderer for X-axis per-row candidate intervals.
 * Each row's candidate intervals are drawn as semi-transparent animated
 * green/white rectangles over the corresponding source row.
 */
export function createXCandidateOverlay(
  candidates: DebugLineCandidates[],
  animationPhase: number, // 0..1, advances each frame
): (ctx: CanvasRenderingContext2D, _imgW: number, _imgH: number, zoom: number) => void {
  return (ctx, _imgW, _imgH, zoom) => {
    for (const lc of candidates) {
      const y = lc.line * zoom;
      const h = zoom;
      for (const [begin, end] of lc.intervals) {
        // Animated green/white: green base with pulsing white overlay
        const x = begin * zoom;
        const w = (end - begin) * zoom;

        // Green base
        ctx.fillStyle = 'rgba(0, 220, 100, 0.25)';
        ctx.fillRect(x, y, w, h);

        // Pulsing white highlight
        const whiteAlpha = 0.1 + 0.15 * Math.sin(animationPhase * Math.PI * 2);
        ctx.fillStyle = `rgba(255, 255, 255, ${whiteAlpha})`;
        ctx.fillRect(x, y, w, h);

        // Border
        ctx.strokeStyle = 'rgba(0, 220, 100, 0.6)';
        ctx.lineWidth = 1;
        ctx.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
      }
    }
  };
}

/**
 * Create a canvas overlay renderer for Y-axis per-column candidate intervals.
 * Each column's candidate intervals are drawn as semi-transparent animated
 * green/white rectangles over the corresponding source column.
 */
export function createYCandidateOverlay(
  candidates: DebugLineCandidates[],
  animationPhase: number, // 0..1, advances each frame
): (ctx: CanvasRenderingContext2D, _imgW: number, _imgH: number, zoom: number) => void {
  return (ctx, _imgW, _imgH, zoom) => {
    for (const lc of candidates) {
      const x = lc.line * zoom;
      const w = zoom;
      for (const [begin, end] of lc.intervals) {
        const y = begin * zoom;
        const h = (end - begin) * zoom;

        // Green base
        ctx.fillStyle = 'rgba(0, 220, 100, 0.25)';
        ctx.fillRect(x, y, w, h);

        // Pulsing white highlight
        const whiteAlpha = 0.1 + 0.15 * Math.sin(animationPhase * Math.PI * 2);
        ctx.fillStyle = `rgba(255, 255, 255, ${whiteAlpha})`;
        ctx.fillRect(x, y, w, h);

        // Border
        ctx.strokeStyle = 'rgba(0, 220, 100, 0.6)';
        ctx.lineWidth = 1;
        ctx.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
      }
    }
  };
}

/**
 * Start a requestAnimationFrame loop that ticks an animation phase value.
 * Returns a cleanup function.
 */
export function startAnimationLoop(
  onTick: (phase: number) => void,
): () => void {
  let rafId: number;
  let start: number | null = null;
  const PERIOD_MS = 2000; // 2-second pulse cycle

  function frame(ts: number) {
    if (start === null) start = ts;
    const elapsed = ts - start;
    const phase = (elapsed % PERIOD_MS) / PERIOD_MS;
    onTick(phase);
    rafId = requestAnimationFrame(frame);
  }

  rafId = requestAnimationFrame(frame);
  return () => cancelAnimationFrame(rafId);
}
