---
name: ColorSpace SIMD 基准测试结论
description: ColorSpace LUT 转换在 ErrorMetric 中占总压缩时间的 36-38%，是主要瓶颈
type: project
---

**基准测试数据**（rounded_panel.png，Release 模式，3 次运行平均）：
- Total: ~234ms
- ToLinear (RgbaU8ToLinear): ~2ms (<1%)
- Search (含 ErrorMetric): ~224ms (95%)
- Reconstruct: ~2.7ms (1%)
- ToSrgb (RgbaLinearToU8): ~0.25ms (<1%)
- **ErrorMetric.ColorSpace (LUT 转换)**: ~84ms (36% of total, 38% of Search)

**结论**：ErrorMetric.MaxError 中的 `LinearToSrgbByte` LUT scatter-gather 是完整压缩流程的最大单一瓶颈，占 Search 阶段近 40% 的时间。

**为什么值得优化**：Search 阶段反复调用 ErrorMetric.MaxError（每次评估一个分割点），每次调用对 6N 个像素做 LUT 查找。这些随机内存访问无法用 `Vector<float>` 向量化，但可以用多项式近似（`pow(x, 2.4)` 和 `pow(x, 1/2.4)`）替代，纯算术运算对 SIMD 友好。

**如何应用**：后续做 ColorSpace SIMD 优化时，优先优化 ErrorMetric 内的 `LinearToSrgbByte` 调用（多项式近似），而非 RgbaU8ToLinear/RgbaLinearToU8 这两个一次性转换。

**SoA 数据布局决策（2026-04-21）**：
用户明确要求做全局 SoA 优化（4 个独立通道数组），不做局部折中。

SoA 的影响范围：ErrorMetric（主要受益者）、Search1D（需改 CopyRect 逻辑）、Resampler（需改 Vector4→Vector<float>）、Compressor（需改大量 *4 索引）。改动量大但机械，纯索引重写。

当前 AoS 布局为 `H×W×4` 交错的 `float[]`，SoA 布局为 4 个 `H×W` 的 `float[]`（R、G、B、A 各一个）。
