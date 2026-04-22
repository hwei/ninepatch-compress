# Memory Index

- [WASM 构建决策和坑](feedback_wasm_gotchas.md) — .NET 10 browser-wasm AOT 构建注意事项、JSExport 语法、JsonSerializer 禁用
- [Web Demo 计划 (Tasks 10-11)](project_web_demo_plan.md) — Vite+React+UnoCSS Web 演示，组件列表和 WASM 集成
- [cs-rewrite 剩余工作交接](project_cs_rewrite_remaining.md) — Phase 12 剩余任务和已知 bug
- [ColorSpace SIMD 基准测试结论](colorspace_benchmark_result.md) — ErrorMetric 内 LUT 转换占总压缩时间 36%，是主要瓶颈
- [Savings check 重构](project_savings_check_refactor.md) — 核心算法不再拒绝低节省结果，调用方自行判断
- [Search1D 阶段一待办](project_search1d_phase1_todo.md) — 三项低风险优化：预计算 sRGB、合并 RGB+Alpha early-exit、消除 pad 写入
- [Search1D 性能优化进度](project_search1d_perf_progress.md) — 已完成 scratch 复用和 box filter 内联计算
