import { useRef, useCallback, useEffect } from 'react';
import { NinePatchOverlay } from './NinePatchOverlay';

export interface ImageViewportProps {
  /** Image source URL. */
  src: string | null;
  /** Image CSS class (e.g. "pixelated"). */
  imgClassName?: string;
  /** Image natural dimensions. */
  width: number;
  height: number;
  /** Display zoom multiplier. */
  zoom: number;
  /** Transparency background CSS class. */
  bgClass: string;
  /** Nine-patch grid in this image's coordinate space, or null. */
  np: { xb: number; xe: number; yb: number; ye: number } | null;
  /** Optional canvas overlay renderer. Called when the canvas mounts / zoom changes.
   *  Receives (ctx, imgW, imgH, zoom). The canvas is already sized W×H. */
  renderOverlay?: (ctx: CanvasRenderingContext2D, imgW: number, imgH: number, z: number) => void;
  /** Children rendered on top (labels, divider handle, etc.). */
  children?: React.ReactNode;
  /** Callback with image-space (x, y) when the mouse moves. */
  onMousePixel?: (imgX: number, imgY: number) => void;
  /** Callback when the mouse leaves the viewport. */
  onMouseLeave?: () => void;
  /** Optional external container ref. */
  containerRef?: React.RefObject<HTMLDivElement | null>;
}

/**
 * Shared image viewport: displays an image with zoom, transparency background,
 * nine-patch grid overlay, optional debug canvas overlay, and mouse tracking.
 * Pixel inspector rendering is handled externally via the onMousePixel callback.
 */
export function ImageViewport({
  src, imgClassName, width, height, zoom, bgClass, np,
  renderOverlay, children, onMousePixel, onMouseLeave, containerRef: externalRef,
}: ImageViewportProps) {
  const W = width * zoom;
  const H = height * zoom;
  const internalRef = useRef<HTMLDivElement>(null);
  const containerRef = externalRef ?? internalRef;
  const canvasRef = useRef<HTMLCanvasElement>(null);

  // Draw overlay whenever renderOverlay or dimensions change
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || !renderOverlay) return;
    canvas.width = W;
    canvas.height = H;
    const ctx = canvas.getContext('2d');
    if (ctx) renderOverlay(ctx, width, height, zoom);
  }, [renderOverlay, W, H, width, height, zoom]);

  const handleMouseMove = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
    const rect = containerRef.current?.getBoundingClientRect();
    if (!rect) return;
    const mx = Math.floor((e.clientX - rect.left) / zoom);
    const my = Math.floor((e.clientY - rect.top) / zoom);
    if (mx >= 0 && mx < width && my >= 0 && my < height) {
      onMousePixel?.(mx, my);
    }
  }, [zoom, width, height, onMousePixel, containerRef]);

  const handleMouseLeave = useCallback(() => {
    onMouseLeave?.();
  }, [onMouseLeave]);

  return (
    <div className="inline-block" style={{ boxShadow: '0 8px 28px rgba(0,0,0,0.4)', border: '1px solid var(--line-hi)' }}>
      <div
        ref={containerRef}
        className={`relative no-select ${bgClass}`}
        style={{ width: W, height: H }}
        onMouseMove={handleMouseMove}
        onMouseLeave={handleMouseLeave}
      >
        {src && (
          <img
            src={src}
            alt=""
            className={`absolute inset-0 ${imgClassName ?? 'pixelated'}`}
            style={{ width: W, height: H, pointerEvents: 'none' }}
            draggable={false}
          />
        )}

        {/* Debug canvas overlay */}
        {renderOverlay && (
          <canvas
            ref={canvasRef}
            className="absolute inset-0 pointer-events-none"
            style={{ width: W, height: H }}
          />
        )}

        {/* Nine-patch grid */}
        {np && (
          <NinePatchOverlay meta={np} width={width} height={height} zoom={zoom} />
        )}

        {children}
      </div>
    </div>
  );
}

/**
 * Read a pixel from an image URL. Returns {r,g,b,a} or null.
 * Uses an off-screen Image + Canvas (one-time read, not for continuous streaming).
 */
export function readPixelFromImage(
  img: HTMLImageElement,
  x: number, y: number,
): { r: number; g: number; b: number; a: number } | null {
  if (x < 0 || x >= img.naturalWidth || y < 0 || y >= img.naturalHeight) return null;
  const canvas = document.createElement('canvas');
  canvas.width = img.naturalWidth;
  canvas.height = img.naturalHeight;
  const ctx = canvas.getContext('2d');
  if (!ctx) return null;
  ctx.drawImage(img, 0, 0);
  const d = ctx.getImageData(x, y, 1, 1).data;
  return { r: d[0], g: d[1], b: d[2], a: d[3] };
}
