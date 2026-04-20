interface PreviewPaneProps {
  imageUrl: string;
  width: number;
  height: number;
  label: string;
  overlay?: React.ReactNode;
}

export function PreviewPane({ imageUrl, width, height, label, overlay }: PreviewPaneProps) {
  return (
    <div className="card">
      <h3 className="label mb-2">{label} ({width}x{height})</h3>
      <div className="relative inline-block">
        <img src={imageUrl} alt={label} className="max-w-full rounded border border-gray-200" style={{ imageRendering: 'pixelated' }} />
        {overlay}
      </div>
    </div>
  );
}
