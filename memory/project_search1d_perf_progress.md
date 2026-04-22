---
name: Search1D 性能优化进度
description: 穷举 (b, e) Search1D 的性能优化记录，已完成 scratch 复用和 box filter 内联计算
type: project
---

## 已完成优化

### 第一轮（commit d18479c）
1. **SoaImage recon 复用**：`Run()` 入口分配一次 `recon`，所有 TryN 复用
2. **ErrorMetric.PassesThreshold**：SIMD early-exit
3. **Resampler Span 重载**：`Downsample1D`/`Upsample1D` 新增 destination buffer 重载

效果：hgrad 100×100 X: 977→593ms, rounded_panel 128×96 X: 354→192ms

### 第二轮（本次，scratch buffer + inline box filter）
1. **ScratchBuffers 类**：`Run()` 预分配 `Region/Down/Up` 三个 w×h float 数组
2. **TryN 零分配**：改用 scratch 数组 + Span 切片，每 TryN 开始时 `Array.Clear(down, 0, downSize)`
3. **ApplyBoxFilterInline**：在 `Resampler` 中新增 on-the-fly weight 计算，消除 `BuildBoxWeights` 的 2D 数组分配

**效果对比（vs 原始 d18479c）**：

| 指标 | 原始 | 优化后 | 提升 |
|------|------|--------|------|
| hgrad 100×100 SearchX | 2597ms | 2214ms | -15% |
| rounded_panel 128×96 SearchX | 836ms | 688ms | -18% |
| rounded_panel 128×96 SearchY | 701ms | 624ms | -11% |
| MeasureError/call | 1.070ms | 0.830ms | -22% |
| Downsample1D | 0.150ms | 0.080ms | -47% |
| Upsample1D | 0.160ms | 0.110ms | -31% |

## 当前瓶颈

ErrorMetric 占 MeasureError 时间的 ~53%（0.439ms / 0.830ms）。
Resampler 占 ~23%。
其余为 region 提取和 recon 写入。

## 下一步（待评估）
1. **Per-K 可行性位图**：大图质变——O(W) 判定所有位置 N=2 失败，搜索立即终止
2. **ErrorMetric 进一步优化**：考虑更粗粒度的 early-exit 或降采样
3. **C3 大图 benchmark**：穷举搜索在 637×822 上极慢（O(L^3)），需要 per-K 位图才能实用

## 关键文件
- `src/NinePatch.Core/Search1D.cs` — ScratchBuffers + TryN 零分配
- `src/NinePatch.Core/Resampler.cs` — ApplyBoxFilterInline 消除 2D 数组
- `src/NinePatch.Core/ErrorMetric.cs` — PassesThreshold early-exit
- `tests/NinePatch.Tests/` — 34 个测试
