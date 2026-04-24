## Why

当前算法在 **straight alpha** 下做 box filter / bilinear 重采样，这在数学上是错的：半透明软边的相邻透明像素会"污染"软边的 RGB（halo / color bleed）。Porter-Duff over 运算只在**预乘 linear** 空间是线性的。

同时，当前误差度量通过 `rgbErr *= max(αo, αr)` 做线性 α 加权——只是对"低 α 时 RGB 误差视觉权重应该变小"的粗糙近似，而且在误差计算里埋了很多 alpha 相关的特殊路径（`alphaWeighted` 分支、alpha 单独 `|Δa|*255`、`PrecomputedSrgb` 缓存原图 sRGB）。

把重采样搬到预乘 linear、误差比较搬到预乘 sRGB，既修掉软边 halo bug、又让误差更接近"人眼实际看到"的合成结果，还能把 4 通道归一为同一套 L∞ 内核——代码更少、更对。

## What Changes

- **Resampler**：box down / bilinear up 从"4 独立通道 straight-alpha 插值" 改为"预乘 linear 空间插值"。顺带修好软边 halo bug。
- **ErrorMetric**：最大误差从"sRGB space per-channel + αWeight"改为"预乘 sRGB 的 R/G/B 通道 + 预乘 linear 的 α 通道，4 路 L∞"。移除 `alphaWeighted` 参数、alpha 通道单独的 `*255` 特殊分支、`ConditionalSelect` 负值 clamp 逻辑。
- **内部类型**：新增三个 `readonly record struct` 明确空间/预乘状态——
  - `SoaImageLinear`：linear RGB + straight α（加载/写回的边界态）
  - `SoaImagePremul`：linear RGB 预乘 α + linear α（Resampler 工作态）
  - `SoaImagePremulSrgb`：sRGB-encoded `(R·α)`/`(G·α)`/`(B·α)` + linear α（ErrorMetric 工作态，同时取代现有 `PrecomputedSrgb` 原图缓存）
- **`SoaImage`**：不再是公共可用的"默认态"类型；要么降级为 alias（= `SoaImageLinear`），要么从 public API 中隐藏。（在 design.md 里决定）
- **对外 API 签名不变**：`NinePatchCompressor.Compress(byte[] rgba, int w, int h, double threshold, ...)` 保持原样。在 XML doc 上钉死"输入必须是 sRGB-encoded、straight-alpha RGBA8"。
- **`NinePatch.Wasm.WasmExports.Compress`** 的 XML/JS doc 追加一句 "input must be sRGB straight-alpha RGBA8, as returned by `canvas.getImageData()`"。
- **α=0 像素的 RGB 输出约定**：unpremul 时除 0 不可避免，约定"α=0 → 输出 R=G=B=0"（不可见像素的 RGB 随便取都合法）。
- **阈值默认值保持 4.0**。数值单位不变（[0,255] sRGB 尺度），但含半透明软边的图在同阈值下**压缩率会变高**（α=0.3 处的 RGB 误差现在权重更小），这是预期且期望的语义变化。
- **ALGORITHM.md** 第 10-38 行（Color space / 1D downsampling / 1D upsampling / Error metric 章节）整段重写。

## Capabilities

### New Capabilities
（无）

### Modified Capabilities
- `core-compression`: 误差度量和重采样的空间语义改变——requirement "Error metric in sRGB space" 重写为"premul sRGB + linear α 的 4 通道 L∞"；requirement "Box downsampling" 和 "Bilinear upsampling" 的适用空间从 linear straight 改为 premul linear（scenario 表层文字可基本保留，但适用语境明确）。

## Impact

**代码**：
- `src/NinePatch.Core/ColorSpace.cs`：新增 `SoaImageLinear/Premul/PremulSrgb` 三个 struct，新增 premultiply/unpremultiply/linear→sRGB 的入口转换函数
- `src/NinePatch.Core/Resampler.cs`：签名和内部循环都切到 `SoaImagePremul`
- `src/NinePatch.Core/ErrorMetric.cs`：主 SIMD 循环简化为"4 路 `Vector.Max(|Δ|)`"；删除 `alphaWeighted` 参数；`PrecomputedSrgb` 删除或合并进 `SoaImagePremulSrgb`
- `src/NinePatch.Core/Compressor.cs`：流程更新——加载后 premultiply、resample 结束后 unpremultiply 再写回；`PrecomputedSrgb` 相关调用替换
- `src/NinePatch.Core/Segmenter.cs`：Segment 输入的 1D signal 数值语义变（从 "linear channel" 到 "premul-sRGB channel" 或 "linear α"），但接口签名不变
- `src/NinePatch.CLI/Program.cs`、`src/NinePatch.Wasm/WasmExports.cs`、`src/NinePatch.Bench/Program.cs`：**无代码改动**（都在 `byte[] rgba` 层调用）；Wasm 加 doc 注释

**测试**：
- `tests/NinePatch.Tests/ErrorMetricTests.cs`：几乎全部断言数值需要重校；`alphaWeighted` 相关 test 删除/改写
- `tests/NinePatch.Tests/SegmenterTests.cs`：含半透像素的断言可能需更新
- `tests/NinePatch.Tests/ColorSpaceTests.cs`：可能新增 premul/unpremul 往返测试
- Bench 样本图的基线数值需重跑

**文档**：
- `ALGORITHM.md` 第 10-38 行
- `openspec/specs/core-compression/spec.md` 相关 requirement（通过本 change 的 specs delta 应用）

**API 兼容**：
- 对外 public API（`NinePatchCompressor.Compress` 签名、`NinePatchMeta` 格式、CLI 参数、WASM JSExports）**无破坏**。
- 含半透明软边的 PNG 在相同 threshold 下输出可能不同（`xb/xe/yb/ye`、`SavingsPct`、`Error2d` 数值变化）——语义变化，非 API 破坏。
