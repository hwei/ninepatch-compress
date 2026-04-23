## 1. WASM 层 — 导出 minLength 参数

- [ ] 1.1 修改 `WasmExports.Compress()` 增加 `int minLength = 8` 参数，传入 `NinePatchCompressor.Compress()`

## 2. TypeScript 层 — 类型与胶水

- [ ] 2.1 `types.ts`: `CompressParams` 接口增加 `minLength: number`
- [ ] 2.2 `WasmLoader.ts`: `WasmInstance` 接口和 `compress()` 函数签名增加 `minLength` 参数

## 3. React 层 — Hook 与 UI

- [ ] 3.1 `useCompressor.ts`: `runCompress` 透传 `minLength`（通过 CompressParams 已含）
- [ ] 3.2 `App.tsx`: `params` 默认状态增加 `minLength: 8`
- [ ] 3.3 `App.tsx`: 压缩参数区域新增"最小拉伸长度"滑块（min=2, max=64, step=1）

## 4. 构建与验证

- [ ] 4.1 `dotnet publish` NinePatch.Wasm 并 copy 到 `public/_framework/`
- [ ] 4.2 启动 Web dev server，验证 UI 控件和参数透传
