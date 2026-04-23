## Context

当前 Web Demo 和 WASM 导出层调用 `NinePatchCompressor.Compress()` 时未传入 `minLength` 参数，使用默认值 8。Core 层已有该参数支持，只需在 WASM → TS → React 全链路透传。

## Goals / Non-Goals

**Goals:**
- 用户在 Web UI 中可调节 `minLength`（2-64，默认 8）
- 全链路透传，不引入中间层转换
- 保持向后兼容（默认值 8 与当前行为一致）

**Non-Goals:**
- 不改变 Core 算法逻辑
- 不修改构建/部署流程（仍手动 copy）
- 不自动同步 WASM 构建产物

## Decisions

1. **WasmExports.Compress() 加参数而非新方法** — 直接在现有方法签名末尾追加 `int minLength = 8`，利用 C# 可选参数保持 JS 调用方无需修改签名。

2. **UI 控件复用现有 ParamField 组件** — App.tsx 已有 `ParamField` 内部组件，新增滑块直接复用，不引入新 UI 抽象。

3. **参数范围 2-64，步长 1** — 2 是理论最小可压缩长度，64 是合理的上限（超过则几乎什么都过滤不掉）。

## Risks / Trade-offs

- [WASM 需手动 copy] → 开发者容易忘记 rebuild + copy，导致前端运行旧代码。后续可自动化构建流程（独立任务）。
