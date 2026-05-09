import { useRef, useCallback } from 'react';
import { NinePatchOverlay } from './NinePatchOverlay';

interface OutputPaneProps {
  url: string | null;
  width: number;
  height: number;
  zoom: number;
  bgClass: string;
  np: { xb: number; xe: number; yb: number; ye: number } | null;
  onMousePixel?: (imgX: number, imgY: number) => void;
  onMouseLeave?: () => void;
}

/**
 * OutputPane: compressed (smaller) image with nine-patch grid on top.
 * Uses the same background class and zoom as the compare view.
 */
export function OutputPane({ url, width, height, zoom, bgClass, np, onMousePixel, onMouseLeave }: OutputPaneProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  const handleMouseMove = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
    const rect = containerRef.current?.getBoundingClientRect();
    if (!rect) return;
    const mx = Math.floor((e.clientX - rect.left) / zoom);
    const my = Math.floor((e.clientY - rect.top) / zoom);
    if (mx >= 0 && mx < width && my >= 0 && my < height) {
      onMousePixel?.(mx, my);
    }
  }, [zoom, width, height, onMousePixel]);

  if (!url) {
    return (
      <div className="panel-flush rounded-lg flex items-center justify-center"
           style={{ height: 180, borderStyle: 'dashed' }}>
        <span className="label" style={{ color: 'var(--text-faint)' }}>
          压缩后输出
        </span>
      </div>
    );
  }

  const W = width * zoom;
  const H = height * zoom;

  return (
    <div className="inline-block" style={{ boxShadow: '0 8px 28px rgba(0,0,0,0.4)' }}>
      <div
        ref={containerRef}
        className={`relative ${bgClass}`}
        style={{ width: W, height: H, border: '1px solid var(--line-hi)' }}
        onMouseMove={handleMouseMove}
        onMouseLeave={onMouseLeave}
      >
        <img
          src={url}
          alt="compressed"
          className="absolute inset-0 pixelated"
          style={{ width: W, height: H, pointerEvents: 'none' }}
          draggable={false}
        />
        {np && (
          <NinePatchOverlay meta={np} width={width} height={height} zoom={zoom} />
        )}
      </div>
    </div>
  );
}
