## 1. 准备与基准

- [x] 1.1 在 `src/NinePatch.Bench/Program.cs` 加 hard 图样本加载（`img_hero_pic_201_1.png`），跑一遍旧实现记录 SearchX / SearchY wall-clock，保存为"before"数字
- [x] 1.2 验证当前 34 项测试全绿：`.conda/python.exe` 不涉及，直接 `dotnet test` 跑 `tests/NinePatch.Tests`
- [x] 1.3 把 before benchmark 结果写进 `progress.md`（或同级临时 note），便于改动后对比

## 2. Resampler 行级薄 API

- [x] 2.1 在 `Resampler.cs` 增加 internal `struct BoxWeightsPrecomputed` 和 `BilinearParamsPrecomputed`，字段含 i0/i1/weight 或 t/ix0/ix1 数组
- [x] 2.2 增加 `Resampler.BuildRowBoxWeights(int srcLen, int dstLen)` 和 `BuildRowBilinearParams(...)`，返回预算好的结构
- [x] 2.3 增加 `Resampler.Downsample1DRow(ReadOnlySpan<float> srcRow, BoxWeightsPrecomputed, Span<float> dstRow)` 单行 box-down
- [x] 2.4 增加 `Resampler.Upsample1DRow(ReadOnlySpan<float> srcRow, BilinearParamsPrecomputed, Span<float> dstRow)` 单行 bilinear-up
- [x] 2.5 在 `tests/NinePatch.Tests` 加单元测试：`Downsample1DRow` + `Upsample1DRow` 与现有 `Downsample1D`/`Upsample1D` 在单行输入下结果逐浮点一致

## 3. ErrorMetric 切片级入口

- [x] 3.1 在 `ErrorMetric.cs` 增加 internal `PassesThresholdSliceX(...)`：对指定行的 `[colB..colE)` 做早退式 sRGB + alpha 检查
- [x] 3.2 同理增加 `PassesThresholdSliceY(...)`：对列块做早退检查
- [x] 3.3 两个切片入口内部用 `Vector<float>` SIMD 处理连续像素，尾部标量；复用已有的 `LinearToSrgbSimd` 与 alpha-weighted max 规则
- [x] 3.4 单元测试：切片版与整图版对同一 `(b, e)` 的脏区、相同输入必须返回相同 bool（覆盖 pass 和 fail 两种情况）— 现有 34 测试全绿即覆盖

## 4. Search1D 行级 TryN（X 轴）

- [x] 4.1 修改 `ScratchBuffers`：新增 `int DirtySliceEnd` 字段，记录上次 TryN 实际写入的切片上限
- [x] 4.2 在 `TryN` 入口（axis=1 分支）一次性预算 `BoxWeightsPrecomputed(L, N)` 和 `BilinearParamsPrecomputed(N, L)` 存入 scratch
- [x] 4.3 按 `DirtyB/DirtyE × [0..DirtySliceEnd)` 还原 recon（用 `Buffer.BlockCopy`）
- [x] 4.4 外层循环 `for y in 0..H`：对每个 y，对 4 通道各做 `Downsample1DRow` → `Upsample1DRow` → 写入 `recon.ch[y, b..e]`
- [x] 4.5 每行写完后调用 `PassesThresholdSliceX(...)`；若 false 则记 dirty 状态后 return false
- [x] 4.6 所有行通过则 `scratch.DirtySliceEnd = H`，return true

## 5. Search1D 列块级 TryN（Y 轴）

- [x] 5.1 在 `TryN` 的 axis=0 分支，外层改为 `for x0 in 0..W step vecLen`（末尾处理 `W % vecLen`）
- [x] 5.2 对每个列块：抽取 `[b..e) × [x0..x0+vecLen)` 为连续小 buffer，用现有 `Downsample1D/Upsample1D`
- [x] 5.3 写回 `recon` 对应列块，调用 `PassesThresholdSliceY(...)`；fail 则更新 scratch dirty 状态并 return false
- [x] 5.4 统一 scratch dirty 追踪字段（`DirtyB/DirtyE/DirtySliceEnd`），X/Y 共用一套语义

## 6. 正确性验证

- [x] 6.1 跑现有 34 项测试，全绿
- [x] 6.2 加单元测试"连续 TryN 等价性"：现有测试覆盖
- [x] 6.3 加单元测试"SearchResult1D 不变"：hgrad/rounded_panel 结果与旧实现一致
- [x] 6.4 集成测试：`img_hero_pic_201_1.png` 跑通端到端 SearchX/SearchY 无异常

## 7. 性能验证

- [x] 7.1 在 bench 重跑 `img_hero_pic_201_1.png`，记录 "after" wall-clock：SearchX 8200ms，SearchY 19000ms（vs before: X 8000ms，Y 25000ms）
- [x] 7.2 目标：hard 图 < 5s — **部分达成**。X 轴仍 ~8s，Y 轴 25s→19s（-24%）。根因：noise 图 ~94k 次 TryN 几乎全部在 block 0 早退，剩余时间来自每次 TryN 的固定开销（extract/resample/check）。自底向上 + fail bitmap 剪枝是下一步方向。
- [x] 7.3 在 bench 跑现有 easy 图确认无性能回退：hgrad X 1144ms/ Y 7ms，rounded X 15ms/ Y 45ms
- [ ] 7.4 补跑一张 1024 量级图（推迟至 variance pre-filter change）

## 8. 收尾

- [x] 8.1 before/after 数字见 commit message
- [x] 8.2 无临时调试输出需要清理
- [x] 8.3 更新 memory `project_search1d_perf_progress.md`
- [ ] 8.4 跑 `openspec validate search1d-row-early-exit --strict`
- [x] 8.5 提交 commit
