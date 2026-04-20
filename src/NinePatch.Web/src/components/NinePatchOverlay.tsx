interface NinePatchOverlayProps {
  meta: {
    xb: number;
    xe: number;
    yb: number;
    ye: number;
  };
  width: number;
  height: number;
}

export function NinePatchOverlay({ meta, width, height }: NinePatchOverlayProps) {
  return (
    <svg
      className="absolute inset-0 pointer-events-none"
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      style={{ width: '100%', height: '100%' }}
    >
      {/* Horizontal lines */}
      <line x1={0} y1={meta.yb} x2={width} y2={meta.yb} stroke="#ef4444" strokeWidth={1} strokeDasharray="4,2" />
      <line x1={0} y1={meta.ye} x2={width} y2={meta.ye} stroke="#ef4444" strokeWidth={1} strokeDasharray="4,2" />
      {/* Vertical lines */}
      <line x1={meta.xb} y1={0} x2={meta.xb} y2={height} stroke="#3b82f6" strokeWidth={1} strokeDasharray="4,2" />
      <line x1={meta.xe} y1={0} x2={meta.xe} y2={height} stroke="#3b82f6" strokeWidth={1} strokeDasharray="4,2" />
      {/* Corner labels */}
      <rect x={meta.xb} y={meta.yb} width={meta.xe - meta.xb} height={meta.ye - meta.yb} fill="rgba(59,130,246,0.1)" stroke="#3b82f6" strokeWidth={1} />
    </svg>
  );
}
