import { getBgClass } from './BackgroundPicker';

export interface PixelInfo {
  /** Label for the top row (e.g. "Original" or "Compressed"). */
  label: string;
  x: number;
  y: number;
  r: number;
  g: number;
  b: number;
  a: number;
  /** If this is a stretch-region sample, show the range. */
  xRange?: [number, number];
  yRange?: [number, number];
}

interface PixelInspectorProps {
  /** Two pixel samples to compare side-by-side. */
  samples: [PixelInfo, PixelInfo];
  visible: boolean;
}

function RgbaSwatch({ r, g, b, a, bgClass }: { r: number; g: number; b: number; a: number; bgClass: string }) {
  const alpha = a / 255;
  return (
    <div className="flex items-center gap-2">
      <div
        className={`relative w-8 h-8 rounded overflow-hidden ${bgClass}`}
        style={{ border: '1px solid var(--line-hi)', backgroundSize: '8px 8px' }}
      >
        <div
          className="absolute inset-0"
          style={{ background: `rgba(${r},${g},${b},${alpha})` }}
        />
      </div>
      <span className="mono text-xs" style={{ color: 'var(--text-dim)' }}>
        R{r} G{g} B{b} A{a}
      </span>
    </div>
  );
}

export function PixelInspector({ samples, visible }: PixelInspectorProps) {
  if (!visible) return null;

  const bgClass = getBgClass('checker-dark');

  return (
    <div
      className="absolute bottom-3 left-1/2 -translate-x-1/2 z-20 pointer-events-none"
      style={{ minWidth: 320 }}
    >
      <div
        className="rounded-lg px-3 py-2 flex flex-col gap-1.5"
        style={{
          background: 'rgba(8,10,14,0.92)',
          border: '1px solid var(--line-hi)',
          backdropFilter: 'blur(8px)',
          boxShadow: '0 4px 16px rgba(0,0,0,0.45)',
        }}
      >
        {samples.map((s, i) => (
          <div key={i} className="flex items-center gap-3">
            <span className="label text-[10px]" style={{ minWidth: 72, color: i === 0 ? '#cfe3ff' : '#ffe3c7' }}>
              {s.label}
            </span>
            <span className="mono text-[11px]" style={{ color: 'var(--text)', minWidth: 80 }}>
              ({s.x},{s.y})
              {s.xRange && (
                <span style={{ color: 'var(--text-faint)' }}>
                  {' '}← [{s.xRange[0]},{s.xRange[1]})
                </span>
              )}
              {s.yRange && (
                <span style={{ color: 'var(--text-faint)' }}>
                  {' '}↕ [{s.yRange[0]},{s.yRange[1]})
                </span>
              )}
            </span>
            <RgbaSwatch r={s.r} g={s.g} b={s.b} a={s.a} bgClass={bgClass} />
          </div>
        ))}
      </div>
    </div>
  );
}
