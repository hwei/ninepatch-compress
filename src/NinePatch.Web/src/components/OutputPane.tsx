import { NinePatchOverlay } from './NinePatchOverlay';

interface OutputPaneProps {
  url: string | null;
  width: number;
  height: number;
  zoom: number;
  bgClass: string;
  np: { xb: number; xe: number; yb: number; ye: number } | null;
}

/**
 * OutputPane: compressed (smaller) image with nine-patch grid on top.
 * Uses the same background class and zoom as the compare view.
 */
export function OutputPane({ url, width, height, zoom, bgClass, np }: OutputPaneProps) {
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
    <div className="inline-block">
      <div
        className={`relative ${bgClass}`}
        style={{ width: W, height: H, border: '1px solid var(--line-hi)' }}
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
