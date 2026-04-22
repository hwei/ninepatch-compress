---
name: Search1D 性能优化进度
description: Search1D 性能优化记录，包含第一轮、第二轮和第一阶段三项优化的完整历史
type: project
---

## 第一阶段（三项优化，已完成）

### #1 预计算原图 sRGB 平面
- `Run()` 开头用 SIMD 把 `img.R/G/B` 转 sRGB 存为 `PrecomputedSrgb`，`PassesThreshold` 直接复用
- 新增 `PrecomputedSrgb` record struct（含 R, G, B, Alpha）
- 新增 `ComputeRgbErrorPrecomputed` — 原图侧直接读取预计算值，只需对 recon 做 LinearToSrgbSimd

### #3 合并 RGB + Alpha 为单次 early-exit 循环
- `PassesThreshold(PrecomputedSrgb, ...)` 中每个向量块先算 RGB max，检查，再算 alpha 检查
- 任一通道超阈值立即退出，不再分两轮遍历

### #4 消除 unchanged recon pad 写入
- `Run()` 开头 recon = original 拷贝一次
- `TryN` 只写 region 部分，pad 不碰
- **坑**：前一次 TryN 的 region 数据残留在 recon 里，会污染下次的误差检查。用 `ScratchBuffers.DirtyB/DirtyE` 追踪，每次 TryN 开头先恢复上一轮 region 到原始值

### 效果对比（vs 第二轮优化后基线）

| 指标 | 基线 | 第一阶段后 | 变化 |
|------|------|-----------|------|
| hgrad 100×100 SearchX | 2214ms | 2027ms | **-8.4%** |
| rounded_panel 128×96 SearchX | 688ms | 679ms | -1.3% |
| rounded_panel 128×96 SearchY | 624ms | 592ms | **-5.1%** |
| MeasureError/call | 0.830ms | 0.860ms | +3.6% |
| Downsample1D | 0.080ms | 0.080ms | 0% |
| Upsample1D | 0.110ms | 0.140ms | 噪声 |

34 测试全过。

## 已完成优化（历史）

### 第一轮（commit d18479c）
1. **SoaImage recon 复用**：`Run()` 入口分配一次 `recon`，所有 TryN 复用
2. **ErrorMetric.PassesThreshold**：SIMD early-exit
3. **Resampler Span 重载**：`Downsample1D`/`Upsample1D` 新增 destination buffer 重载

效果：hgrad 100×100 X: 977→593ms, rounded_panel 128×96 X: 354→192ms

### 第二轮（commit 4a5c309，scratch buffer + inline box filter）
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
- `src/NinePatch.Core/Search1D.cs` — ScratchBuffers + TryN + 预计算 sRGB + dirty region 追踪
- `src/NinePatch.Core/ErrorMetric.cs` — PrecomputedSrgb + PassesThreshold 合并循环 + ComputeRgbErrorPrecomputed
- `src/NinePatch.Core/Resampler.cs` — ApplyBoxFilterInline 消除 2D 数组
- `tests/NinePatch.Tests/` — 34 个测试
