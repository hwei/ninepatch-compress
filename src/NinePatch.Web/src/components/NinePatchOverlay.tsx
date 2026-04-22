interface NinePatchOverlayProps {
  meta: { xb: number; xe: number; yb: number; ye: number };
  width: number;
  height: number;
  zoom?: number;
  subtle?: boolean;
}

/**
 * Nine-patch grid overlay: cyan X-lines, amber Y-lines,
 * and corner intersection dots.
 */
export function NinePatchOverlay({ meta, width, height, zoom = 1, subtle = false }: NinePatchOverlayProps) {
  const W = width * zoom;
  const H = height * zoom;
  const xb = meta.xb * zoom;
  const xe = meta.xe * zoom;
  const yb = meta.yb * zoom;
  const ye = meta.ye * zoom;

  return (
    <svg
      className="absolute inset-0 pointer-events-none"
      width={W}
      height={H}
      style={{ overflow: 'visible' }}
    >
      <line x1={xb} y1={-4} x2={xb} y2={H + 4}
            stroke="var(--np-x)" strokeWidth={1} shapeRendering="crispEdges" />
      <line x1={xe} y1={-4} x2={xe} y2={H + 4}
            stroke="var(--np-x)" strokeWidth={1} shapeRendering="crispEdges" />

      <line x1={-4} y1={yb} x2={W + 4} y2={yb}
            stroke="var(--np-y)" strokeWidth={1} shapeRendering="crispEdges" />
      <line x1={-4} y1={ye} x2={W + 4} y2={ye}
            stroke="var(--np-y)" strokeWidth={1} shapeRendering="crispEdges" />

      {!subtle && ([[xb, yb], [xe, yb], [xb, ye], [xe, ye]] as [number, number][]).map(([cx, cy], i) => (
        <circle key={i} cx={cx} cy={cy} r={2.5}
                fill="var(--bg)" stroke="var(--accent)" strokeWidth={1} />
      ))}
    </svg>
  );
}
