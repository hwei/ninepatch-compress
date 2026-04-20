---
name: cs-rewrite 剩余工作和交接
description: Phase 12 剩余任务和已知 bug，供下个 session 继续
type: project
---

**当前状态**: 56/61 任务完成。Phase 1-11 全部完成，Phase 12 剩 4 个任务。

**待完成任务**:

1. **12.1 CLI vs Python 对比测试** — 对同一批输入运行 .NET CLI 和 Python 实现，对比压缩结果和九宫格元数据。需先修复已知 bug。

2. **12.3 AOT CLI 构建** — `dotnet publish -c Release -r win-x64`。CLI.csproj 已配置 PublishAot+RuntimeIdentifier。

3. **12.4 WASM 模块构建** — `dotnet publish -c Release` 带 browser-wasm RID。WASM.csproj 已配置。

4. **12.5 删除 python-impl/** — 依赖 12.1 确认 .NET 实现行为与 Python 一致后执行。

**已知 Bug**（需修复后才能完成 12.1）:

1. `BoundaryError` 对非均匀图像返回 999f — `Compressor.cs` 中 upsample 尺寸不匹配保护（`up.Length != region.Length`）。
2. `error_2d=255` 对带 margin 的非均匀图像（如 rounded_panel.png 128x96）— 根因在 `Compress2D`/`ReconstructStretched` 的 Y 轴维度处理。
3. 均匀图像 roundtrip 正确，bug 仅在非均匀图像+margin 时出现。

**WASM 注意事项**: NinePatch.Wasm.csproj 中 **不使用** `PublishAot=true` — .NET 10 browser-wasm 有独立的 AOT 流水线，NativeAOT 不适用。

**Web Demo 运行方式**:
```
cd src/NinePatch.Web && npm run dev
```
需要先把 WASM 构建产物拷贝到 `public/_framework/`。

**关键文件**:
- 核心 API: `src/NinePatch.Core/NinePatchCompressor.cs`
- Compressor: `src/NinePatch.Core/Compressor.cs`
- 任务列表: `openspec/changes/cs-rewrite/tasks.md`
