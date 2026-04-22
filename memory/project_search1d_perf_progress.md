---
name: Search1D 性能优化进度
description: Search1D 性能优化记录，包含 X 轴行级早退和 Y 轴预计算权重优化
type: project
---

## Search1D Row Early-Exit（commit a9b6b39，已合并）

### 已完成
1. **X 轴行级早退**：`TryN_X` 改用 `Downsample1DRow/Upsample1DRow` + 预计算权重，每行处理后检查误差，早退
2. **Y 轴列块级早退**：`TryN_Y` 按 vecLen=8 分块处理，每块处理后检查误差
3. **Y 轴预计算权重**：新增 `Downsample1DCol/Upsample1DCol` 使用预计算权重，消除每次 TryN 的权重重算
4. **Dirty region 追踪统一**：所有 TryN 使用统一的 DirtyB/DirtyE/DirtySliceEnd 恢复机制

### 效果对比

| 图像 | SearchX（优化前→后） | SearchY（优化前→后） |
|------|---------------------|---------------------|
| hgrad 100×100 | 2214ms→1144ms | 10ms→7ms |
| rounded_panel 128×96 | 688ms→15ms | 624ms→45ms |
| hard noise 435×511 | ~8s→8.2s | ~25s→19s（-24%） |

34 测试全绿。

## Y 轴性能优化探索

### 根因
Y 轴 `TryN_Y` 预计算了 box 权重和 bilinear 参数，但实际 resample 调用的是 `Downsample1D/Upsample1D`，这些函数内部**重新计算权重**。预计算结果被忽略。

### 已实施修复
在 `Resampler.cs` 中新增 `Downsample1DCol/Upsample1DCol` ——使用预计算权重的列级 resample 函数，带 SIMD 优化（`Vector<float>` 对 regionW 列并行）。`TryN_Y` 改用这两个函数。

### 尝试但失败的方案
- **全宽一次处理**：去掉分块早退，一次处理整行宽度。结果 hard noise 图从 19s 暴增至 477s——没有分块早退，每个 TryN 都要处理全部 435 列。

### 为什么不能进一步提速
对于 noise 图，~94k 次 TryN 几乎每次都在 block 0 就早退。剩余的 ~19s 主要来自每 TryN 的 BlockCopy 提取/写回 + SIMD resample + 误差检查。要突破需要算法级改变（减少 TryN 次数、quick-reject 预检查等）。

## 关键文件
- `src/NinePatch.Core/Search1D.cs` — TryN_X（行级早退）+ TryN_Y（列块级早退 + 预计算权重）
- `src/NinePatch.Core/Resampler.cs` — Downsample1DRow/Upsample1DRow + Downsample1DCol/Upsample1DCol
- `src/NinePatch.Core/ErrorMetric.cs` — PassesThresholdSliceX/PassesThresholdSliceY
- `tests/NinePatch.Tests/` — 34 个测试
