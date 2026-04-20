import type { NinePatchMeta } from '../wasm/types';

interface MetadataDisplayProps {
  meta: NinePatchMeta;
  onDownload: () => void;
}

export function MetadataDisplay({ meta, onDownload }: MetadataDisplayProps) {
  return (
    <div className="card">
      <h3 className="label mb-3">压缩结果</h3>

      <div className="grid grid-cols-2 gap-2 text-sm">
        <div>
          <span className="text-gray-500">原始尺寸</span>
          <p className="font-mono">{meta.original_width} x {meta.original_height}</p>
        </div>
        <div>
          <span className="text-gray-500">压缩尺寸</span>
          <p className="font-mono">{meta.compressed_width} x {meta.compressed_height}</p>
        </div>
        <div>
          <span className="text-gray-500">Nine-Patch 区域</span>
          <p className="font-mono">xb={meta.xb}, xe={meta.xe}, yb={meta.yb}, ye={meta.ye}</p>
        </div>
        <div>
          <span className="text-gray-500">分割数</span>
          <p className="font-mono">Nx={meta.nx}, Ny={meta.ny}</p>
        </div>
        <div>
          <span className="text-gray-500">误差</span>
          <p className="font-mono">X={meta.error_x.toFixed(2)}, Y={meta.error_y.toFixed(2)}, 2D={meta.error_2d.toFixed(2)}</p>
        </div>
        <div>
          <span className="text-gray-500">压缩率</span>
          <p className="font-mono text-green-600 font-bold">{meta.savings_pct.toFixed(1)}%</p>
        </div>
      </div>

      <div className="mt-4 flex gap-2">
        <button className="btn" onClick={onDownload}>下载压缩图片</button>
      </div>
    </div>
  );
}
