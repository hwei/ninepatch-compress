## Context

当前代码在"linear RGB + straight α"单一空间做所有事：load 后 sRGB→linear、不预乘；Resampler 四通道独立做 box/bilinear；ErrorMetric 再把 linear 转回 sRGB 比较、并用 `max(αo, αr)` 给 RGB 加权、α 走一条独立的 `|Δa|*255` 分支。

两个问题：

1. **Straight-alpha 下的插值数学上错**。Porter-Duff over 是 `αo*Cfg + (1-αo)*Cbg`——这是"预乘颜色"的线性叠加。直接对 straight α 的 RGB 取平均，会让透明相邻像素（RGB 任意）污染软边颜色，产生 halo。
2. **`max(αo, αr)` 是低质量的感知权重**。它是对"透明区域 RGB 不可见"的线性近似。更合适的做法是直接在"预乘后的颜色"上度量——因为那是合成到任意背景上可见的部分。

同时，ErrorMetric 里 `alphaWeighted` bool、`PrecomputedSrgb`、α 单独 SIMD 循环这些分支把代码拉长且难维护。

用户已明确：
- 重采样切到预乘 linear
- 误差切到预乘 sRGB 的 4 通道 L∞（α 保持 linear、RGB 过 sRGB）
- 对外 API 不变、阈值默认 4.0 不变

## Goals / Non-Goals

**Goals:**
- 修掉 straight-alpha 下 Resampler 的软边 halo/bleed bug
- 把 4 通道误差统一为同一个 SIMD L∞ 内核，删除 `alphaWeighted`、`PrecomputedSrgb`、α 特殊路径
- 内部用三个独立类型表达"空间/预乘状态"，让函数签名即文档
- `NinePatchCompressor.Compress` 签名和 `NinePatchMeta` 输出格式不变；CLI / WASM 对外接口不变

**Non-Goals:**
- 不支持 "linear 字节输入" 或 "任意色彩空间" 的 Core API 参数化（入口永远是 sRGB straight RGBA8）
- 不解析 PNG 的 `sRGB`/`gAMA`/`iCCP` chunk——ImageSharp 的 `Image.Load<Rgba32>` 已把色彩 profile 规范化为 sRGB
- 不新增 `--raw-format=linear` 类 CLI 参数（未来独立 change）
- 不改 Segmenter / Optimize / auto-retry 骨架
- 不做感知颜色空间升级（OKLab / CIEDE2000 等）
- 不做性能优化；只要求 bench 没有明显 regression

## Decisions

### D1: 三个独立 struct 表达三个空间，而非 flag / 泛型

```
SoaImageLinear       { R, G, B, A }   // R,G,B: linear;     A: linear straight
SoaImagePremul       { R, G, B, A }   // R,G,B: linear·α;   A: linear
SoaImagePremulSrgb   { R, G, B, A }   // R,G,B: sRGB(linear·α); A: linear
```

三者底层都是 `readonly record struct { float[] R, G, B, A; int Width, Height }`，**只是类型名不同**，无额外字段。函数签名携带空间契约：

```csharp
SoaImagePremul    Premultiply(SoaImageLinear)
SoaImageLinear    Unpremultiply(SoaImagePremul)
SoaImagePremulSrgb ToSrgb(SoaImagePremul)
```

**替代方案考虑：**
- `enum ColorState { Linear, Premul, PremulSrgb }` 挂在 `SoaImage` 上：运行时检查、签名不携带契约、容易串味
- `SoaImage<TState>` phantom type：.NET 泛型改造面大、阅读负担（每行 generic constraint）
- 状态保持文档约定、单一 `SoaImage`：最省事也最易串味，现行方案

硬编码三个类型最 pragmatic，状态集合有限（3 个）且稳定；如果未来加 OKLab / P3，这是专门 change 去重构的信号，不是"先泛型占坑"的理由。

### D2: `SoaImageLinear` 是否保留为 public API 类型

当前 `SoaImage` 是 `public readonly record struct`，被 Bench 等直接使用。改动后：

- 重命名 `SoaImage` → `SoaImageLinear`（保持 public；Bench 等调用方跟随改名）
- 或保留 `SoaImage` 作为 `SoaImageLinear` 的 type alias（C# 没有真的 alias，只能 `using SoaImage = ...`，不跨程序集）

**决定：** 重命名 `SoaImage` → `SoaImageLinear`。理由：Bench 和 Tests 在本仓库内，跟随改名代价低；公开 `SoaImage` 的语义本来就隐含"linear straight"，显式命名更清晰。

`SoaImagePremul` 和 `SoaImagePremulSrgb` 标 **internal**——调用方不应该直接构造，只通过 `ColorSpace.Premultiply` / `ToSrgb` 过渡。

### D3: α=0 像素的 RGB 输出约定

从 `SoaImagePremul` 转回 `SoaImageLinear` 时，`R = R_premul / α`。α=0 时除 0。

**决定：** α=0 → 输出 R=G=B=0。

理由：
- α=0 像素合成时对任何背景都不可见，RGB 写什么都合法
- `R_premul` 在预乘空间本来就是 0（由于 R*α=0），`0/0` 取 0 最自然
- Resampler 在预乘空间做 box filter，α=0 的"透明邻居"不会污染软边——这是本次改动**修好的 bug**
- 不需要额外保留一份 straight 副本

SIMD 里用 `ConditionalSelect` 或 `Vector.Max(α, ε)` 处理；选前者，语义清楚。

### D4: `SoaImagePremulSrgb` 兼任原图缓存

现 `PrecomputedSrgb` 是 `ErrorMetric` 为避免 Search1D 每次迭代重算原图 `linearToSrgb` 而加的热路径缓存。新 `SoaImagePremulSrgb` 的载体正好是"预算好的原图误差空间形态"。

**决定：** 删除 `PrecomputedSrgb`。原图的 `SoaImagePremulSrgb` 在 `Compressor.Compress` 入口构造一次，传入 Segment / Optimize / ErrorMetric 做原图侧参数。

重建图那侧在 SIMD 循环内就地转 sRGB——reconstructed 每次都变，不可预缓存。

### D5: ErrorMetric 的 SIMD 内核

单个 L∞ 内核，4 通道对称：

```
for each vector chunk i:
  dR = |orig_srgb.R[i] - LinearToSrgbSimd(recon_premul.R[i])|
  dG = |orig_srgb.G[i] - LinearToSrgbSimd(recon_premul.G[i])|
  dB = |orig_srgb.B[i] - LinearToSrgbSimd(recon_premul.B[i])|
  dA = |orig_srgb.A[i] - recon_premul.A[i]|              // α 走线性，不过 sRGB
  vMax = max(vMax, dR, dG, dB, dA) * 255
```

- 4 个 `Vector.Max` 构成单调归约
- α 通道不走 `LinearToSrgbSimd`（参考 D5 rationale：α 是线性混合系数，不是感知量）
- 最后 `*255` 统一放到归约后而非每通道内
- `PassesThreshold` 版本在 vMax 超阈值时早退

参数表：
- `MaxError(SoaImagePremulSrgb orig, SoaImagePremul recon) → float`
- `PassesThreshold(SoaImagePremulSrgb orig, SoaImagePremul recon, float threshold) → bool`

签名去掉 `alphaWeighted`，不再有双路径。

### D6: Resampler 入口类型

```csharp
SoaImagePremul DownsampleX(SoaImagePremul src, int dstWidth)
SoaImagePremul UpsampleX(SoaImagePremul src, int dstWidth)
// Y 通过 Transpose+X 实现（已有）
```

内部循环**完全不变**——4 个 `float[]` 独立 box/bilinear。把类型名从 `SoaImage` 换成 `SoaImagePremul` 就行；唯一功能变化是 α 这一路现在承载 linear α（之前也是 linear α），RGB 那三路承载 R·α/G·α/B·α（之前是 straight R）。

### D7: Segmenter 的 1D signal 语义

**现状（已核对 `Segmenter.cs:25-70`、`ComputeSrgbArray`、`ComputeErrorArray`）**：Segmenter 内部 Phase 1 已经是 "在 linear 空间 box down + bilinear up，再 `LinearToSrgbSimd * 255` 后做 L∞ 误差比较"——这个结构和新体系天然同构，只要把 caller 传进来的 signal 从 "linear straight 某通道" 换成 "premul linear 某通道"，Segmenter 内部的 `LinearToSrgb` 就会自动把它变成 premul sRGB。**signal 来源是 `SoaImagePremul` 某通道的 `float[]` 直接引用，零拷贝**。

注意：这**不是**我最初在 D8 / Migration Plan 里说的 `SoaImagePremulSrgb.R`——那会让 Segmenter 内部的 `LinearToSrgb` 变成"对已 encode 过的值再做一次 encode"，错误。正确的 signal source 是 `SoaImagePremul`（linear premul）。

**一个必须处理的例外：α 通道不过 sRGB encoding。** Segmenter 当前无条件调用 `LinearToSrgbSimd`，α 通道喂进去会被错误地做非线性压缩，导致 `|Δα|*255` 不再成立。

**决定：** Segmenter 的相关函数（`Segment`、`ComputeSrgbArray`、`ComputeErrorArray`、`VerifySegmentIndependent`）新增一个 `bool isLinearChannel = false` 参数向下传递：
- `false`（默认，RGB 通道）：保留现有 `LinearToSrgb` 路径
- `true`（α 通道）：跳过 sRGB encode，直接 `up * 255` / `signal * 255`

调用点（`SqueezeHorizontal` / `SqueezeVertical` 里对 4 通道的循环）对 index=3（α 通道）传 `isLinearChannel: true`。

threshold 单位在所有通道统一为 [0, 255]（详见 D7b）。

**验证方式：** 已有 Segmenter 单测用抽象的 float signal 和 RGB 路径，和空间语义正交；改动后仍通过。新增至少一个 α-path 单测（`isLinearChannel: true`），验证 α 误差不经 sRGB encode。

### D7b: α 通道的 threshold 单位取舍

`RGB 通道 error` 单位是 premul-sRGB [0, 255]；`α 通道 error` 单位是 linear α [0, 255]（`|Δα|*255`）。两者数值尺度对齐，但**感知影响不同**——同样的 Δ=5/255，α 通道实际合成到背景的视觉差约为 RGB 的 1/3~1/8（取决于被合成像素的亮度和 α 绝对值）。

**决定：** 保持单一 `threshold` 参数、α 通道与 RGB 通道共享 threshold。承认"α 通道在感知上偏紧"是保守侧取舍，写进 `ALGORITHM.md`。

替代方案考虑过：
- `alphaThreshold` 独立参数：API 表面扩大，默认值难定；无数据支撑时不加旋钮
- α threshold 内部固定倍数放大（如 ×3）：magic number，违反最小惊讶原则
- α 不进 Segment：会破坏半透发光/柔光边的 segment 精度，和"4 通道 intersection"的既有设计冲突

如果实施阶段 bench 显示样本图因 α 通道阻塞导致压缩率损失 >5%，再起独立 change 引入 `--alpha-threshold`。

### D8: Compressor 数据流

```
Input byte[] rgba
    │
    │ ColorSpace.DecodeSrgbRgba8ToLinear  (新：原 RgbaU8ToLinear 重命名)
    ▼
SoaImageLinear
    │
    │ ColorSpace.Premultiply
    ▼
SoaImagePremul origPremul
    │                      ├─ ColorSpace.ToPremulSrgb → SoaImagePremulSrgb origSrgb  (仅给 ErrorMetric 原图侧)
    │                      │
    │  Search (Segment / Intersect / Squeeze / Optimize)
    │  Segmenter 从 origPremul 拎单通道 float[] 作为 signal（RGB: isLinearChannel=false, α: true）
    │  内部 Segment 自己做 linear→sRGB encode 得到 premul-sRGB 误差；α 通道 bypass
    │  2D ErrorMetric 对比 origSrgb 和 reconstructed SoaImagePremul
    ▼
best (xb, xe, yb, ye, Nx, Ny)
    │
    │ 9 region assembly (Resample on SoaImagePremul)
    ▼
SoaImagePremul compressedPremul
    │
    │ ColorSpace.Unpremultiply   (α=0 → RGB=0)
    ▼
SoaImageLinear compressedLinear
    │
    │ ColorSpace.EncodeLinearToSrgbRgba8  (新：原 RgbaLinearToU8 重命名)
    ▼
Output byte[] rgba
```

Reconstruction for Error2d 报告：compressed → 反向 assembly → `SoaImagePremul` → `SoaImagePremulSrgb` → 对比原图 `SoaImagePremulSrgb`。

### D9: WASM / CLI / Web 不改代码

- `WasmExports.Compress` 只加 XML doc + 对应 JS 侧 TypeScript defs / README 的 "input must be sRGB straight-alpha RGBA8 as returned by canvas.getImageData()" 一句
- `CLI/Program.cs` 和 Web 后端都是 byte[] 层调用，与内部空间解耦
- Bench 只改 import 类型名（`SoaImage` → `SoaImageLinear`）

### D10: 重采样 SIMD 内核保留

虽然数学上"预乘"让 box/bilinear 内核数值含义变了，但代码路径复用度 100%——4 个独立 float 数组上的同样 SIMD 循环。可能的例外是 **bilinear upsample 的边界行为**：lerp(A, B, t) 在预乘空间和 straight 空间运算一致；无变化。

## Risks / Trade-offs

- **[Risk] 阈值语义在含半透像素的图上变化，用户可能感到"同样参数下结果不同"** → Mitigation: 在 proposal/changelog 明确这是语义修正；CLI `--help` / WASM doc 说明 threshold 单位不变但对半透更宽松。不改默认值 4.0 避免二次扰动。

- **[Risk] 现有 ErrorMetricTests 断言数值大量失效** → Mitigation: 测试作为实现任务的一部分重写；保留若干"纯 RGB"（全 α=1）样本的断言不变作为回归锚点。

- **[Risk] 样本图 golden xb/xe 移动引发 tests/Bench 失败** → Mitigation: 样本图对比脚本单独跑一次，列出哪些 `xb/xe/yb/ye` 变了；人工确认视觉结果合理后接受新 baseline。

- **[Risk] α=0 像素 unpremul 除 0，SIMD 实现写错会出 NaN 并污染下游** → Mitigation: 在 `ColorSpace.Unpremultiply` 用 `Vector.ConditionalSelect(α == 0, 0, R/α)`；加单测覆盖 α=0 边界。

- **[Trade-off] `SoaImagePremul` / `SoaImagePremulSrgb` 是 internal，外部测试要跑 ErrorMetric 得走 `InternalsVisibleTo`** → 当前测试项目已有 `InternalsVisibleTo`（`PrecomputedSrgb` 是 internal），沿用。

- **[Trade-off] 三个 struct 意味着类型冗余——Width/Height/PixelCount/Index 方法重复** → Mitigation: 提一个 `internal interface ISoaPlanes { int Width; int Height; float[] R,G,B,A; }` 复用；或直接复制几行（代码量 <30 行/type）。倾向复制——接口带来虚派发风险（除非 struct static interface dispatch，.NET 8+ 可用）。

## Migration Plan

**无对外数据迁移**。内部：

1. 先落 `ColorSpace` 新类型和转换函数 + 单测（`SoaImageLinear → Premul → PremulSrgb → Linear` 往返）
2. 改 `Resampler` 签名换类型（行为零变化）
3. 改 `ErrorMetric` SIMD 内核（核心行为变更发生在此步）
4. 改 `Compressor.cs` 拼装流程
5. 删 `PrecomputedSrgb`
6. 给 `Segmenter.Segment` 及其内部 helper 增加 `bool isLinearChannel` 参数，α 通道路径跳过 `LinearToSrgb`；更新 `SqueezeHorizontal`/`SqueezeVertical` 在第 4 通道传 `isLinearChannel: true`；signal 来源从 `SoaImage` 某通道改为 `SoaImagePremul` 某通道
7. 更新 tests（预计 ErrorMetricTests 最大改动）
8. 更新 ALGORITHM.md + WASM XML doc
9. Bench 跑一遍确认无性能 regression、输出 `SavingsPct` 的新基线

每一步都独立编译通过、tests 尽量保持过（3 之后 ErrorMetric 相关 tests 集中更新一次）。

## Open Questions

1. ~~**Segmenter 的 signal 来源统一**~~ → **Resolved（D7）**：signal 来自 `SoaImagePremul` 某通道（不是 `SoaImagePremulSrgb`），零拷贝直接引用；Segmenter 内部保留现有 `LinearToSrgb` 路径；α 通道需加 `isLinearChannel: true` 参数 bypass。

2. ~~**α 通道在 Segmenter 的 threshold 语义**~~ → **Resolved（D7b）**：所有通道共享同一个 threshold。α 通道感知上偏紧是保守侧取舍，写进 `ALGORITHM.md`。若实测压缩率损失 >5% 再起独立 change 加 `--alpha-threshold`。

3. **Alpha 通道的"感知均匀性"问题**：α=0.1 到 α=0.2 与 α=0.8 到 α=0.9 在 L∞ 下权重相同，但后者视觉上跳变更大（合成结果差距大）。暂不处理——α 的人眼敏感度曲线是另一回事，本 change 聚焦 RGB 预乘模型。
