import type { CompressParams } from '../wasm/types';

interface ParameterControlsProps {
  params: CompressParams;
  onChange: (params: CompressParams) => void;
}

export function ParameterControls({ params, onChange }: ParameterControlsProps) {
  const update = (key: keyof CompressParams, value: number) => {
    onChange({ ...params, [key]: value });
  };

  return (
    <div className="card space-y-4">
      <h3 className="label mb-2">压缩参数</h3>

      <div>
        <div className="flex justify-between">
          <label className="label" htmlFor="threshold">误差阈值 (Threshold)</label>
          <span className="text-sm text-gray-500">{params.threshold.toFixed(1)}</span>
        </div>
        <input
          id="threshold"
          type="range"
          className="input-range"
          min={0}
          max={32}
          step={0.5}
          value={params.threshold}
          onChange={(e) => update('threshold', Number(e.target.value))}
        />
        <p className="text-xs text-gray-400">sRGB 每通道最大误差，默认 4.0</p>
      </div>

      <div>
        <div className="flex justify-between">
          <label className="label" htmlFor="margin">最小角尺寸 (Margin)</label>
          <span className="text-sm text-gray-500">{params.margin}</span>
        </div>
        <input
          id="margin"
          type="range"
          className="input-range"
          min={0}
          max={32}
          step={1}
          value={params.margin}
          onChange={(e) => update('margin', Number(e.target.value))}
        />
        <p className="text-xs text-gray-400">最小角区域大小，默认 0</p>
      </div>

      <div>
        <div className="flex justify-between">
          <label className="label" htmlFor="minSavings">最低压缩收益 (Min Savings)</label>
          <span className="text-sm text-gray-500">{params.minSavings.toFixed(0)}%</span>
        </div>
        <input
          id="minSavings"
          type="range"
          className="input-range"
          min={0}
          max={80}
          step={5}
          value={params.minSavings}
          onChange={(e) => update('minSavings', Number(e.target.value))}
        />
        <p className="text-xs text-gray-400">低于此百分比则跳过压缩，默认 30%</p>
      </div>
    </div>
  );
}
