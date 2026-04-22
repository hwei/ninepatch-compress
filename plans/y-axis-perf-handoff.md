# Search1D Y 轴性能优化交接

## 背景

`search1d-row-early-exit` change 已提交 commit `a9b6b39`。X 轴行级早退已实现且性能显著改善，但 **Y 轴仍然是瓶颈**。

## 当前性能数据

| 图像 | SearchX | SearchY |
|------|---------|---------|
| hgrad 100x100 | 1133ms | 10ms |
| rounded 128x96 | 19ms | 62ms |
| hard noise 435x511 | 8s | **25s** |

目标：hard 图 < 5s（X+Y 总和）。

## Profile 结果

对 Y 轴 `TryN_Y` 加了分块计时，发现：
- 第一个 TryN_Y(0,100,50)：extract=0.0ms, **down=0.4ms, up=0.7ms**, check=0.5ms
- 后续 TryN 全部 ~0.0ms（单调用 < 50μs）
- 对 noise 图，几乎每个 TryN 都在 block 0 就 fail（早退生效）
- 但 ~94k 次 TryN × 50μs = **~4.7s 纯开销**（实测 25s 说明实际更慢）

## 根因分析

`TryN_Y` 调用了 `BuildRowBoxWeights` 和 `BuildRowBilinearParams` 预计算权重，但**实际 resample 用的是 `Resampler.Downsample1D/Upsample1D`，这些函数内部会重新计算权重**。预计算的权重被忽略了。

具体来说，TryN_Y 的循环：
```csharp
for each channel (4x):
  for each row y in [b..e):
    Buffer.BlockCopy  // extract: 4*len*blockW bytes per row
  Downsample1D(region, regionW, regionH, n, 0, down)  // 重算权重！
  Upsample1D(down, regionW, n, len, 0, up)            // 预计算了 bilinear 但没用到！
  for each row y in [b..e):
    Buffer.BlockCopy  // write back
```

问题：
1. `Downsample1D` → 内部调用 `ApplyBoxFilterInline`，每调用一次都重算 box 权重
2. `Upsample1D` → 内部调用 `ApplyBilinearUpsample`，预计算了插值参数但每次 TryN 只调用一次（比 Downsample 好）
3. BlockCopy 提取/写回：每通道每行一次，4 通道 × len 行 × blockW bytes = 大量小拷贝

## 修复方向

### 方案 A：Y 轴也用预计算权重 + 行级 Resample（推荐）

X 轴的 `TryN_X` 已经用了 `Downsample1DRow/Upsample1DRow` + 预计算权重。Y 轴需要对列做同样的事。

但 Y 轴的列数据不是连续的（stride = w），不能直接复用 `Downsample1DRow`。需要：

1. 在 Resampler 中新增 `Downsample1DCol` / `Upsample1DCol` ——按列方向操作，接受 (srcArray, srcStartIndex, srcStride, srcLen, weights, dstArray, dstStartIndex, dstStride, dstLen)
2. 或者在 TryN_Y 中先把列数据提取到连续 buffer（已做），用预计算权重做 resample，再写回

方案 2 的伪代码：
```csharp
// 提取列区域到连续 buffer（已有）
// 用预计算权重做 resample ——关键改动
for each ch:
  for each dstCol d in [0..n):  // n 个目标行
    float sum = 0;
    for each srcRow s in [srcStart[d]..srcEnd[d]):
      // srcRow s 对应的数据在 scratch.Region[s * regionW .. (s+1)*regionW)
      // 但这里我们要对每个 column x 做加权平均
      // 因为 regionW 很小（vecLen=8），可以逐 pixel 处理
      for each x in [0..regionW):
        sum += scratch.Region[s * regionW + x] * weight[d, s];
    scratch.Down[d * regionW + x] = sum;
```

但这样写会变成 Python 级别的循环，失去 SIMD 优势。更好的做法是：

### 方案 B：Y 轴改为"逐行处理"而非"逐列块处理"

Y 轴的 box-down 是**沿列方向**（把 len 行压缩成 n 行），但每行的数据是独立的。可以改为：

1. 对每一行 y，提取该行的 `[x0..x0+blockW)` 列数据（vecLen 个像素，连续）
2. 这 vecLen 个像素作为 SIMD lane，同时对 n 个目标行做 box-down
3. 但 box-down 的权重只依赖行索引，不依赖列索引，所以可以对 vecLen 列同时应用相同的权重

具体实现：
```csharp
// 对每个目标行 d in [0..n):
//   对每列 x in [0..blockW):
//     down[d * blockW + x] = sum over s of region[s * blockW + x] * weight[d, s]
// 内层循环对 x 可以用 SIMD（blockW <= vecLen）
```

### 方案 C：Quick-reject 优化

对于 noise 图，大多数 TryN 在 block 0 就 fail。可以在做 resample 之前加一个快速的预检查：
- 检查输入区域的像素方差，如果方差 > 某个阈值，直接 reject
- 但这改变了算法语义，可能影响 SearchResult1D 的正确性

## 已完成的改动

| 文件 | 改动 |
|------|------|
| `Resampler.cs` | 新增 `BoxWeightsPrecomputed`, `BilinearParamsPrecomputed`, `Downsample1DRow`, `Upsample1DRow`, `BuildRowBoxWeights`, `BuildRowBilinearParams` |
| `ErrorMetric.cs` | 新增 `PassesThresholdSliceX`, `PassesThresholdSliceY`（SIMD 早退） |
| `ColorSpace.cs` | 新增 `LinearToSrgbFloat()` 标量多项式，修复 SIMD vs LUT 数值不匹配 |
| `Search1D.cs` | 重写 `TryN_X`（行级早退）和 `TryN_Y`（列块级早退），统一 dirty tracking |

## 关键 Bug 修复记录

**标量尾 sRGB 转换不匹配**：`PassesThresholdSliceX` 的标量尾用 `LinearToSrgbByte(recon.R[i]) / 255f`（LUT-based）与 `origSrgb.R[i]`（SIMD 多项式预计算）比较，对同一输入给出不同结果。修复：新增 `ColorSpace.LinearToSrgbFloat()` 标量多项式版本，在标量尾用一致的方法。

## 下一步

1. 选一个方案（推荐方案 A 或 B）实现 Y 轴预计算权重
2. 验证 34 测试全绿
3. 确认 hgrad/rounded 无回退
4. 确认 hard noise 435x511 SearchY < 3s
5. 更新 tasks.md 和 memory
