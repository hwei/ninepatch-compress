## Context

`Search1D.Run` 枚举所有 `(b, e)` 长度区间，对每个区间用 binary search over N 找最小通过阈值的 N。对 **hard-to-compress** 图片，quick-reject（N=L/2 的试探）几乎总是 fail，每个 `(b, e)` 都要完整跑一次验收路径：

1. `TryN` 按 4 通道分别做 box-down（L → N）+ bilinear-up（N → L），写入 `recon` 的 `[b..e]` 列区域
2. `ErrorMetric.PassesThreshold(origSrgb, recon, threshold)` 在 **整张 W×H 图** 做 SIMD sRGB 差异检查（有 early-exit，但扫到不同像素才触发）

两个浪费：
- 行间完全独立（X 轴 box-down 和 bilinear-up 都是逐行算子），却先把所有行算完再判定
- 错误检查扫整图，但 `[b..e]` 以外的像素是 recon 初始化时拷贝的原图、必然差异为 0

样本 `img_hero_pic_201_1.png`（435×511）X 轴约 95k TryN，单次约 1ms，总计 ~100s。同样算法对 1024×1024 要数分钟不可用。

## Goals / Non-Goals

**Goals:**
- hard 图 Search1D 端到端 wall-clock 下降至少一个数量级（目标 435×511 hard 图 < 5s，1024×1024 hard 图可完成）
- 输出 `SearchResult1D` 与旧实现逐比特一致（穷举顺序不变、验收判定不变）
- 零新依赖，代码改动集中在 `Search1D.cs` + `ErrorMetric.cs`
- 现有 30 项测试全绿

**Non-Goals:**
- 不做算法性剪枝（方案 3/4/5 留给后续 change）
- 不改变 `(b, e)` 枚举顺序或 binary search 结构
- 不优化 easy-to-compress 图（它们本来就快；此改动对它们最多持平）
- 不改 Resampler 的公共 API（仍按"整个区域一次性"调用，只是调用粒度变小）

## Decisions

### Decision 1: 验收路径按行早退（X 轴）/ 按 vecLen 列块早退（Y 轴）

**什么**：`TryN` 不再按"4 通道先全跑完 Resampler，再 `PassesThreshold`"流程。改为外层循环 over 行 `y`（X 轴）或列块 `x0..x0+vecLen`（Y 轴），内层对该切片执行 1D box-down、bilinear-up，同时立即在该切片上做 sRGB diff + alpha-weighted max，首个像素超阈值立即 `return false`。

**为什么**：
- X 轴 box-down/bilinear-up 是 per-row 独立算子，切到单行粒度对数学等价
- Y 轴 per-column 独立，但列的 stride = W，直接按单列跑会破坏 SIMD；按 `Vector<float>.Count` 个相邻列一组（数据连续）做，既保持 SIMD 友好又能早退
- 早退期望收益：hard 图多数 TryN 在处理到第 k 行/列块时就 fail（k ≪ H），省掉 `(H-k)/H` 比例的 Resampler + 检查开销

**替代方案**：
- *并行 SIMD 跨所有行*：无法早退，不解决核心问题
- *一次处理多行的 tile*：复杂度高，收益有限（tile 内仍要算完才能判定）

### Decision 2: `ErrorMetric` 新增 `PassesThreshold` 行级/列块级入口

**什么**：保留现有整图版 `PassesThreshold(PrecomputedSrgb, SoaImage, float)` 不动（外部调用者如测试、`MaxError` 路径不受影响）。新增 internal 重载接收"某一行/列块的 4 通道切片 + 对应 origSrgb 切片"，内部只做这段 SIMD 比较。

**为什么**：
- 不破坏现有 API 与测试
- 切片级 SIMD 仍可用 `Vector<float>` 跨相邻像素（行内 L 个像素、或列块内 vecLen 个列各一行）
- `PrecomputedSrgb` 仍然一次性预计算整张图，复用于每次 TryN 的切片比较（索引偏移即可）

### Decision 3: `ScratchBuffers.DirtyB/DirtyE` 的语义扩展

**什么**：当 TryN 早退时，只有部分行/列块被写入了新数据；其余行/列块仍等于原图（因 `recon` 初始化时拷贝了原图）。新增 `DirtyRowsEnd`（或等价字段），记录"上次 TryN 实际写到第几行/列块为止"。下次 TryN 进入时，只还原 `[DirtyB..DirtyE) × [0..DirtyRowsEnd)` 这个"真正被脏"的矩形。

**为什么**：
- 避免还原本来没写过的行，省掉大量 `Buffer.BlockCopy`
- 保证下次 `TryN` 开头 recon 在未涉及区域 == 原图，行级早退里 `[b..e] 之外的像素不用再比较`（它们必然差异为 0）这一不变量继续成立

### Decision 4: Y 轴列块粒度 = `Vector<float>.Count`

**什么**：Y 轴的外层循环单元是 `vecLen`（AVX2 下 = 8）个相邻列，内层对这 vecLen 列同时做 box-down（列向，读 `[b..e]` 行）和 bilinear-up，把 vecLen 列作为 SIMD lane 并行。剩余列数（`W % vecLen`）用标量尾处理。

**为什么**：
- 列向 stride 访问对 SIMD 不友好，但一行之内相邻 vecLen 个列在内存中是连续的 → 以行为载入单位、vecLen 列平行是正确用法
- 和现有 `ErrorMetric` 里 `Vector<float>` 的用法一致

### Decision 5: Resampler 复用还是重写 1D 行级算子

**什么**：不新增 1D 行级算子。调用现有 `Resampler.Downsample1D`/`Upsample1D` 时传入 `srcH=1`（X 轴逐行）或 `srcW=vecLen`（Y 轴列块），复用其实现。scratch buffer 的 Region/Down/Up 继续按最大 `L×H` 或 `W×L` 预分配，但每次 TryN 只用到其中很小一段。

**为什么**：
- 最低改动面积
- Resampler 的 box 权重对 srcLen=L 的输入是 per-column 独立的，逐行调用数学正确
- 缺点：每行/列块进入 Resampler 都要重算 box 权重 — 但权重只依赖 `(L, N)`，可以在 TryN 入口预算一次权重表、逐行调用时复用（见 Decision 6）

### Decision 6: Box 权重预算在 TryN 入口

**什么**：`TryN` 入口算一次 `(L→N)` 的 box 权重和 `(N→L)` 的 bilinear 插值参数，存在 `ScratchBuffers` 里；行/列块循环里直接用。

**为什么**：
- 消除 Resampler 当前 `ApplyBoxFilterInline` 每行重算权重的开销
- 需新增 Resampler 内部 API（或 Search1D 自持一小段 1D 算子）接受预算好的权重 — 选择前者，在 Resampler 里加一个 `Downsample1DRow` 之类的薄函数

## Risks / Trade-offs

- **[风险] 行级循环 JIT 开销可能吃掉早退收益**：对 easy-to-compress 图，quick-reject 通常 pass、binary search 触发，每次 TryN 要跑完全部行，此时行级外层循环是纯开销。
  → **Mitigation**：把"所有行都 pass"的走完路径做得和旧路径一样紧凑（每行内部 SIMD 照常），避免每行一次函数调用；实测 easy 图（hgrad/rounded_panel）性能不回退。

- **[风险] 脏区追踪出 bug 导致 recon 残留脏数据**：下次 TryN 读到未被还原的旧 recon，误判 pass。
  → **Mitigation**：新增单元测试"连续两次 TryN 应与独立单次 TryN 等价"；调试路径允许全量还原（fallback）。

- **[风险] Y 轴列块尾处理 / `W % vecLen != 0`**：尾列用标量路径，复杂度 ≪ 主体，但写错会让结果不一致。
  → **Mitigation**：测试覆盖非 vecLen 倍数的宽度（如 435、411）。

- **[Trade-off] 不做算法性剪枝**：本 change 只降常数，hard 图复杂度仍 O(W²·H)。1024 可用但可能还要几秒到十几秒。
  → **Acceptable**：先拿常数级收益；算法性优化留给后续 change，避免一次改太多风险累积。

- **[Trade-off] Decision 6 增加 Resampler API 面积**：新增一个私有/internal 薄函数。
  → **Acceptable**：改动局部，不影响现有公开 API。

## Migration Plan

1. 加行级早退路径 + dirty tracking 扩展，跑现有 30 测试全绿
2. 加新单元测试：(a) 行级早退等价于整图 PassesThreshold；(b) 连续多次 TryN 等价于独立单次
3. 在 `src/NinePatch.Bench` 加入 `img_hero_pic_201_1.png` 作为 hard 图 benchmark
4. 比较 before/after wall-clock；若无明显退化并达成目标则合并

**回滚**：改动集中在两个文件，revert 两个 commit 即可；`SearchResult1D` 结果不变意味着没有下游数据兼容性问题。

## Open Questions

- Decision 6 的权重预算位置：放在 `ScratchBuffers` 里（Search1D 拥有）还是作为 Resampler 的 context 对象？倾向前者，因为只有 Search1D 热路径需要。
- Y 轴列块尾是否合并进 AVX2 `Vector<float>` mask-load 路径，还是标量处理？倾向标量（简单、尾列数少），profile 后再评估。
