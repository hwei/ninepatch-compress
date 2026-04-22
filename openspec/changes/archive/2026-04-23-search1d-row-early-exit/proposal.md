## Why

Search1D 的穷举 `(b, e)` 对 hard-to-compress 图片非常慢：每个候选 `(b, e, N=L/2)` 都完整跑 box-down + bilinear-up 后再对 **全图** W×H 做 sRGB threshold check，即便只有 `[b..e]` 列的像素和原图不同。实际样本 `img_hero_pic_201_1.png`（435×511）观察到接近分钟级延迟，1024×1024 级别不可用。瓶颈出在两处：(1) Resampler 一次跑满整个区域所有行，(2) ErrorMetric 扫满 W×H。因为 hard 图几乎所有候选都 fail，quick-reject 触发频繁，这两项浪费被放大数万倍。

## What Changes

- 重写 `Search1D.TryN` 为 **行级早退**（X 轴按行、Y 轴按 vecLen 列块）：每处理一行/列块就算完 sRGB error，首个超阈值像素即 `return false`，跳过剩余的 Resampler 和 ErrorMetric 工作。
- `ErrorMetric` 新增脏区扫描入口：只在 `[b..e]` 列（X 轴）或 `[b..e]` 行（Y 轴）范围内比较 recon 与 precomputed sRGB original，不再无脑扫整图。
- `ScratchBuffers` 的 `DirtyB/DirtyE` 恢复逻辑保留（不能删，跨 TryN 调用仍要还原），但因 TryN 可能在某一行就早退，dirty 状态追踪改为"最后一次写入到第几行/列块"，下次调用只需还原已写入部分。
- 所有改动保持 `SearchResult1D` 返回值逐比特一致（穷举顺序和验收判定不变）。

## Capabilities

### New Capabilities

（无）

### Modified Capabilities

- `core-compression`: 补充 Search1D `TryN` 的行级早退与脏区扫描约束；对外结果不变，但新增"内部验收路径必须与全图等价"的 requirement 以固化正确性契约。

## Impact

- 修改代码：`src/NinePatch.Core/Search1D.cs`、`src/NinePatch.Core/ErrorMetric.cs`
- 不改动：`Resampler.cs`（1D 行级算子本来就是每行独立，直接复用现有 `Downsample1D`/`Upsample1D`，只是改成每次调用 1 行×1 通道）
- 测试：现有 30 项测试必须全绿；新增 benchmark 样本 `img_hero_pic_201_1.png`（或等价 hard 图）跑通端到端
- 不影响：CLI/Wasm/Web 公开 API；`NinePatchMeta` 字段；margin auto-retry 逻辑
- 性能目标：hard 图（435×511 级）从当前分钟级降到 5s 内；1024×1024 hard 图可完成搜索（当前实际不可用）
