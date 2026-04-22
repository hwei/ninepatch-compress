---
name: Search1D 性能优化阶段一待办
description: 三项优化已全部完成，详见 project_search1d_perf_progress.md
type: project
---

## 状态：已完成

三项优化均已实施，34 测试全过。详细性能数据见 `project_search1d_perf_progress.md`。

### 已知坑（实施中验证）
- Buffer.BlockCopy 只接受 Array，不接受 Span
- ApplyBoxFilter 使用 += 累积，复用 down buffer 前必须 Array.Clear
- **Dirty region 污染**：#4 优化中 TryN 只写 region 部分，前一次 TryN 的 region 数据会残留并污染下次误差检查。必须用 ScratchBuffers.DirtyB/DirtyE 追踪并在每次 TryN 开头恢复上一轮 region 到原始值
