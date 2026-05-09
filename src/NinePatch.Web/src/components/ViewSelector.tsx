export type ViewId = 'compare' | 'compressed' | 'debug-x' | 'debug-y';

const VIEWS: { id: ViewId; label: string; shortLabel: string }[] = [
  { id: 'compare',   label: '原图 · 重建对比', shortLabel: '对比' },
  { id: 'compressed', label: '压缩输出',        shortLabel: '压缩' },
  { id: 'debug-x',    label: 'Debug X 行候选',  shortLabel: 'X 候选' },
  { id: 'debug-y',    label: 'Debug Y 列候选',  shortLabel: 'Y 候选' },
];

interface ViewSelectorProps {
  value: ViewId;
  onChange: (id: ViewId) => void;
  hasResult: boolean;
}

export function ViewSelector({ value, onChange, hasResult }: ViewSelectorProps) {
  return (
    <div className="flex items-center gap-2">
      <span className="label">视图</span>
      <div className="seg">
        {VIEWS.map(v => {
          const active = v.id === value;
          const disabled = (v.id === 'compressed' || v.id === 'debug-x' || v.id === 'debug-y') && !hasResult;
          return (
            <button
              key={v.id}
              data-active={active}
              disabled={disabled}
              onClick={() => onChange(v.id)}
              title={v.label}
              style={disabled ? { opacity: 0.4 } : undefined}
            >
              {v.shortLabel}
            </button>
          );
        })}
      </div>
    </div>
  );
}
