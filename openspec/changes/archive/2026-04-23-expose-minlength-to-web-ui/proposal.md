## Why

算法重构（commit c488755）引入了 `minLength` 参数控制可压缩段的最小长度，默认值为 8。当前 WASM 导出和 Web Demo 未暴露此参数，用户无法控制最小拉伸区域长度，导致对精细纹理的控制力不足。

## What Changes

- WASM 导出接口 `WasmExports.Compress()` 新增 `minLength` 参数（默认 8）
- TypeScript 类型定义 `CompressParams` 增加 `minLength: number`
- `WasmLoader.compress()` 函数签名透传 `minLength`
- `useCompressor` hook 透传 `minLength`
- Web UI 左侧面板新增"最小拉伸长度"滑块控件（默认 8，范围 2-64，步长 1）

## Capabilities

### New Capabilities

无新增能力规格，纯参数透传。

### Modified Capabilities

- `web-ui-params`: Web UI 压缩参数组新增 `minLength` 控件，与现有 `threshold` 和 `margin` 并列

## Impact

- `src/NinePatch.Wasm/WasmExports.cs` — JSExport 方法签名变更
- `src/NinePatch.Web/src/wasm/types.ts` — TypeScript 类型扩展
- `src/NinePatch.Web/src/wasm/WasmLoader.ts` — 胶水层签名更新
- `src/NinePatch.Web/src/hooks/useCompressor.ts` — hook 参数透传
- `src/NinePatch.Web/src/App.tsx` — UI 控件新增
- `src/NinePatch.Web/public/_framework/` — 需重新 build WASM 并 copy
