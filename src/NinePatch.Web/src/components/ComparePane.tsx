import { useState, useRef, useCallback, useEffect } from 'react';
import { NinePatchOverlay } from './NinePatchOverlay';

interface ComparePaneProps {
  originalUrl: string | null;
  reconstructedUrl: string | null;
  width: number;
  height: number;
  zoom: number;
  bgClass: string;
  np: { xb: number; xe: number; yb: number; ye: number } | null;
  compressing: boolean;
  onMousePixel?: (imgX: number, imgY: number) => void;
  onMouseLeave?: () => void;
}

/**
 * ComparePane: original (left) + reconstructed (right) with draggable
 * vertical divider. Nine-patch grid lines on TOP layer covering both halves.
 */
export function ComparePane({
  originalUrl, reconstructedUrl,
  width, height, zoom, bgClass,
  np, compressing,
  onMousePixel, onMouseLeave,
}: ComparePaneProps) {
  const [ratio, setRatio] = useState(0.5);
  const containerRef = useRef<HTMLDivElement>(null);
  const draggingRef = useRef(false);

  const W = width * zoom;
  const H = height * zoom;

  const onMouseDown = useCallback((e: React.MouseEvent | React.TouchEvent) => {
    draggingRef.current = true;
    e.preventDefault();
  }, []);

  useEffect(() => {
    function onMove(e: MouseEvent | TouchEvent) {
      if (!draggingRef.current || !containerRef.current) return;
      const clientX = 'touches' in e ? e.touches[0]?.clientX : e.clientX;
      if (clientX === undefined) return;
      const rect = containerRef.current.getBoundingClientRect();
      const r = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
      setRatio(r);
    }
    function onUp() { draggingRef.current = false; }
    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
    window.addEventListener('touchmove', onMove, { passive: false });
    window.addEventListener('touchend', onUp);
    return () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
      window.removeEventListener('touchmove', onMove);
      window.removeEventListener('touchend', onUp);
    };
  }, []);

  const dividerX = ratio * W;

  const handleMouseMove = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
    const rect = containerRef.current?.getBoundingClientRect();
    if (!rect) return;
    const mx = Math.floor((e.clientX - rect.left) / zoom);
    const my = Math.floor((e.clientY - rect.top) / zoom);
    if (mx >= 0 && mx < width && my >= 0 && my < height) {
      onMousePixel?.(mx, my);
    }
  }, [zoom, width, height, onMousePixel]);

  const handleMouseLeave = useCallback(() => {
    onMouseLeave?.();
  }, [onMouseLeave]);

  return (
    <div
      ref={containerRef}
      className={`relative ${bgClass} no-select`}
      style={{ width: W, height: H }}
      onMouseMove={handleMouseMove}
      onMouseLeave={handleMouseLeave}
    >
      {/* Original — clipped to left of divider */}
      {originalUrl && (
        <div
          className="absolute top-0 left-0 overflow-hidden"
          style={{ width: dividerX, height: H }}
        >
          <img
            src={originalUrl}
            alt="original"
            className="absolute top-0 left-0 pixelated"
            style={{ width: W, height: H, pointerEvents: 'none' }}
            draggable={false}
          />
        </div>
      )}

      {/* Reconstructed — clipped to right of divider */}
      {reconstructedUrl ? (
        <div
          className="absolute inset-y-0 right-0 overflow-hidden"
          style={{ width: W - dividerX }}
        >
          <img
            src={reconstructedUrl}
            alt="reconstructed"
            className="absolute top-0 right-0 pixelated"
            style={{ width: W, height: H, pointerEvents: 'none' }}
            draggable={false}
          />
        </div>
      ) : (
        <div
          className="absolute inset-y-0 right-0 flex items-center justify-center"
          style={{ width: W - dividerX }}
        >
          <div
            className="absolute inset-0"
            style={{ background: 'rgba(12, 14, 18, 0.55)', backdropFilter: 'blur(2px)' }}
          />
          <div className="relative text-center">
            {compressing ? (
              <div className="flex flex-col items-center gap-2">
                <div className="w-6 h-6 rounded-full border-2 border-transparent"
                     style={{
                       borderTopColor: 'var(--accent)',
                       borderRightColor: 'var(--accent)',
                       animation: 'spin 0.8s linear infinite',
                     }} />
                <span className="label" style={{ color: 'var(--text-dim)' }}>压缩中…</span>
              </div>
            ) : (
              <span className="label" style={{ color: 'var(--text-faint)' }}>重建图 · 待压缩</span>
            )}
          </div>
          <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
        </div>
      )}

      {/* ── Top layer ── */}
      {/* Side labels */}
      <div
        className="absolute top-2 left-2 px-2 py-1 rounded text-[10px] font-semibold tracking-wide mono"
        style={{ background: 'rgba(0,0,0,0.55)', color: '#cfe3ff', backdropFilter: 'blur(4px)' }}
      >
        ORIGINAL
      </div>
      <div
        className="absolute top-2 right-2 px-2 py-1 rounded text-[10px] font-semibold tracking-wide mono"
        style={{
          background: 'rgba(0,0,0,0.55)',
          color: reconstructedUrl ? '#ffe3c7' : 'rgba(255,227,199,0.4)',
          backdropFilter: 'blur(4px)',
        }}
      >
        RECONSTRUCTED
      </div>

      {/* Nine-patch grid — top layer, spans both halves */}
      {np && (
        <NinePatchOverlay meta={np} width={width} height={height} zoom={zoom} />
      )}

      {/* Divider */}
      <div
        className="absolute top-0 bottom-0"
        style={{ left: dividerX, width: 0, pointerEvents: 'none' }}
      >
        <div
          className="absolute top-0 bottom-0"
          style={{
            left: -1, width: 2,
            background: 'rgba(255,255,255,0.95)',
            boxShadow: '0 0 0 1px rgba(0,0,0,0.35)',
          }}
        />
        <div
          onMouseDown={onMouseDown}
          onTouchStart={onMouseDown}
          className="absolute"
          style={{
            left: -14, top: '50%', transform: 'translateY(-50%)',
            width: 28, height: 44, borderRadius: 10,
            background: '#fff',
            boxShadow: '0 2px 8px rgba(0,0,0,0.35), 0 0 0 1px rgba(0,0,0,0.12)',
            cursor: 'ew-resize', pointerEvents: 'auto',
            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 2,
          }}
          title="拖动对比"
        >
          <span style={{ width: 2, height: 14, background: '#9aa1b1', borderRadius: 1 }} />
          <span style={{ width: 2, height: 14, background: '#9aa1b1', borderRadius: 1 }} />
        </div>
      </div>
    </div>
  );
}
