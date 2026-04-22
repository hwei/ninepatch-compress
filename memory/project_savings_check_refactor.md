---
name: Savings check 重构
description: 核心算法不再拒绝低节省结果，调用方自行判断是否采纳
type: project
---

Savings check 已从核心算法移到调用方。`RunFullPipeline` 和 `NinePatchCompressor.Compress` 不再接受 `minSavings` 参数，不再返回 `SavingsTooLow` 状态。`CompressStatus` 枚举已移除 `SavingsTooLow` 值。

**Why:** 核心算法只负责压缩质量和正确性，是否采纳应由外部调用方（CLI、Web UI、外部工具）根据自身需求决定。之前 `SavingsTooLow` 直接丢弃压缩结果，导致调用方无法观察实际节省数据。

**How to apply:** 调用方应检查 `meta.SavingsPct` 自行判断。CLI 层在 stderr 输出警告但仍保存文件；Web UI 层显示黄色警告 banner 但仍展示结果。
