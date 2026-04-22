const ZOOM_OPTIONS = [1, 2, 4, 8];

interface ZoomPickerProps {
  value: number;
  onChange: (z: number) => void;
}

export function ZoomPicker({ value, onChange }: ZoomPickerProps) {
  return (
    <div className="flex items-center gap-2">
      <span className="label">Zoom</span>
      <div className="seg">
        {ZOOM_OPTIONS.map(z => (
          <button
            key={z}
            data-active={value === z}
            onClick={() => onChange(z)}
            className="mono"
          >
            {z}x
          </button>
        ))}
      </div>
    </div>
  );
}
