## 1. 准备与基准

- [ ] 1.1 在 `src/NinePatch.Bench/Program.cs` 加 hard 图样本加载（`img_hero_pic_201_1.png`），跑一遍旧实现记录 SearchX / SearchY wall-clock，保存为"before"数字
- [ ] 1.2 验证当前 30 项测试全绿：`.conda/python.exe` 不涉及，直接 `dotnet test` 跑 `tests/NinePatch.Tests`
- [ ] 1.3 把 before benchmark 结果写进 `progress.md`（或同级临时 note），便于改动后对比

## 2. Resampler 行级薄 API

- [ ] 2.1 在 `Resampler.cs` 增加 internal `struct BoxWeightsPrecomputed` 和 `BilinearParamsPrecomputed`，字段含 i0/i1/weight 或 t/ix0/ix1 数组
- [ ] 2.2 增加 `Resampler.BuildRowBoxWeights(int srcLen, int dstLen)` 和 `BuildRowBilinearParams(...)`，返回预算好的结构
- [ ] 2.3 增加 `Resampler.Downsample1DRow(ReadOnlySpan<float> srcRow, BoxWeightsPrecomputed, Span<float> dstRow)` 单行 box-down
- [ ] 2.4 增加 `Resampler.Upsample1DRow(ReadOnlySpan<float> srcRow, BilinearParamsPrecomputed, Span<float> dstRow)` 单行 bilinear-up
- [ ] 2.5 在 `tests/NinePatch.Tests` 加单元测试：`Downsample1DRow` + `Upsample1DRow` 与现有 `Downsample1D`/`Upsample1D` 在单行输入下结果逐浮点一致

## 3. ErrorMetric 切片级入口

- [ ] 3.1 在 `ErrorMetric.cs` 增加 internal `PassesThresholdSliceX(PrecomputedSrgb, SoaImage recon, int row, int colB, int colE, float threshold, bool alphaWeighted)`：对指定行的 `[colB..colE)` 做早退式 sRGB + alpha 检查，首个超阈值像素 return false
- [ ] 3.2 同理增加 `PassesThresholdSliceY(PrecomputedSrgb, SoaImage recon, int rowB, int rowE, int colBlock, int colBlockLen, float threshold, bool alphaWeighted)`：对 `[rowB..rowE) × [colBlock..colBlock+colBlockLen)` 的列块做早退检查
- [ ] 3.3 两个切片入口内部用 `Vector<float>` SIMD 处理连续 `vecLen` 像素，尾部标量；复用已有的 `LinearToSrgbSimd` 与 alpha-weighted max 规则
- [ ] 3.4 单元测试：切片版与整图版对同一 `(b, e)` 的脏区、相同输入必须返回相同 bool（覆盖 pass 和 fail 两种情况）

## 4. Search1D 行级 TryN（X 轴）

- [ ] 4.1 修改 `ScratchBuffers`：新增 `int DirtyRowsEnd = 0` 字段（或等价），记录上次 TryN 实际写入的行数上限
- [ ] 4.2 在 `TryN` 入口（axis=1 分支）一次性预算 `BoxWeightsPrecomputed(L, N)` 和 `BilinearParamsPrecomputed(N, L)` 存入 scratch
- [ ] 4.3 按 `DirtyB/DirtyE × [0..DirtyRowsEnd)` 还原 recon（用 `Buffer.BlockCopy`）
- [ ] 4.4 外层循环 `for y in 0..H`：对每个 y，对 4 通道各做 `Downsample1DRow` → `Upsample1DRow` → 写入 `recon.ch[y, b..e]`
- [ ] 4.5 每行写完后调用 `PassesThresholdSliceX(..., row=y, b, e)`；若 false 则记 `scratch.DirtyRowsEnd = y + 1`、`scratch.DirtyB = b`、`scratch.DirtyE = e` 后 return false
- [ ] 4.6 所有行通过则 `scratch.DirtyRowsEnd = H`，return true

## 5. Search1D 列块级 TryN（Y 轴）

- [ ] 5.1 在 `TryN` 的 axis=0 分支，外层改为 `for x0 in 0..W step vecLen`（末尾处理 `W % vecLen`）
- [ ] 5.2 对每个列块：抽取 `[b..e) × [x0..x0+vecLen)` 为连续 `len × vecLen` 小 buffer（4 通道分别），用现有 `Downsample1D/Upsample1D` 或新增 `*Row` 的 vecLen-wide 版本
- [ ] 5.3 写回 `recon` 对应列块，调用 `PassesThresholdSliceY(...)`；fail 则更新 scratch dirty 状态（改为记录 `DirtyColBlockEnd`）并 return false
- [ ] 5.4 统一 scratch dirty 追踪字段（考虑改成 `DirtySliceBegin/DirtySliceEnd/DirtySliceKind`），X/Y 共用一套语义

## 6. 正确性验证

- [ ] 6.1 跑现有 30 项测试，全绿
- [ ] 6.2 加单元测试"连续 TryN 等价性"：同一 img 先 `TryN(b1,e1,N1)` 再 `TryN(b2,e2,N2)`，结果必须与分别用新 scratch 单次 TryN 结果一致（覆盖两个 TryN 都 fail、都 pass、一 pass 一 fail 三组合）
- [ ] 6.3 加单元测试"SearchResult1D 不变"：对 tests/samples 下所有现有图片 `SearchX/SearchY` 在改动前后返回相同 `(Begin, End, N)`
- [ ] 6.4 集成测试：`img_hero_pic_201_1.png` 跑通端到端 `RunFullPipeline` 无异常

## 7. 性能验证

- [ ] 7.1 在 bench 重跑 `img_hero_pic_201_1.png`，记录 "after" wall-clock，与 "before" 对比
- [ ] 7.2 目标：hard 图（435×511）< 5s；若未达成，profile 找下个瓶颈（可能是 Resampler 内部 `ApplyBoxFilterInline` 重算权重，或 SIMD 路径未命中）
- [ ] 7.3 在 bench 跑现有 easy 图（hgrad, rounded_panel）确认无性能回退（相差 < 20%）
- [ ] 7.4 补跑一张 1024 量级图（如有可手工上采 hgrad），确认进入可用区间（至少完成搜索）

## 8. 收尾

- [ ] 8.1 把 before/after 数字写进 PR 描述 / commit message
- [ ] 8.2 清理任何临时 `progress.md` 或调试输出
- [ ] 8.3 更新 memory `project_search1d_perf_next.md`：行级早退 + 脏区限定已完成；下一步候选（方案 3/4）仍待决
- [ ] 8.4 跑 `openspec validate search1d-row-early-exit --strict` 确认 change 文档合法
- [ ] 8.5 提交 commit；合并后用 `openspec archive search1d-row-early-exit` 归档
