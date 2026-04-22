const BG_OPTIONS = [
  { id: 'checker-dark',  label: '暗格', className: 'bg-checker-dark' },
  { id: 'checker-light', label: '亮格', className: 'bg-checker-light' },
  { id: 'black',         label: '黑',   className: 'bg-black-solid' },
  { id: 'white',         label: '白',   className: 'bg-white-solid' },
];

function BgSwatch({ opt, active }: { opt: typeof BG_OPTIONS[number]; active: boolean }) {
  return (
    <span
      className={`inline-block w-4 h-4 rounded ${opt.className}`}
      style={{
        boxShadow: active
          ? 'inset 0 0 0 1.5px var(--accent)'
          : 'inset 0 0 0 1px rgba(255,255,255,0.15)',
        backgroundSize: '8px 8px',
        backgroundPosition: '0 0, 0 4px, 4px -4px, -4px 0px',
      }}
    />
  );
}

interface BackgroundPickerProps {
  value: string;
  onChange: (id: string) => void;
}

export function BackgroundPicker({ value, onChange }: BackgroundPickerProps) {
  return (
    <div className="flex items-center gap-2">
      <span className="label">BG</span>
      <div className="seg">
        {BG_OPTIONS.map(opt => {
          const active = value === opt.id;
          return (
            <button
              key={opt.id}
              data-active={active}
              onClick={() => onChange(opt.id)}
              title={opt.label}
            >
              <BgSwatch opt={opt} active={active} />
              <span>{opt.label}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

export function getBgClass(bgId: string): string {
  const opt = BG_OPTIONS.find(o => o.id === bgId);
  return opt ? opt.className : 'bg-checker-dark';
}
