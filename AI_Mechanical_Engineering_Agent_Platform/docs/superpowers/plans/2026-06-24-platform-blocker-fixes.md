# 平台 Blocker 修复计划归档

本文是平台骨架 Blocker 修复的历史实施计划归档。

## 目标

在进入下一阶段前修复基础平台 Blocker，包括 solution、Module 标准目录、`module.yaml` 加载、Gateway QualityGate、Storage 契约和 self-check 验证。

## 架构约束

- 只处理平台边界问题。
- 不接真实 CAD、OpenClaw、LLM 或数据库。
- Module manifest 优先来自 `module.yaml`。
- Gateway 消息必须经过 `QualityGate`。

## 已完成任务

1. 创建 `.sln`，并把现有 `.csproj` 加入 solution。
2. 补齐 8 个 Module 的标准目录和 `.gitkeep`。
3. 新增 `ModuleManifestLoader`，优先读取 `module.yaml`，失败时使用 fallback 并记录审计。
4. 更新 Gateway 派发流程，把 `AgentOutput` 转为 `ReviewReport` 并由 Gatekeeper 裁决。
5. 新增 Storage 抽象接口。
6. 更新 self-check，验证 solution、Module 结构、manifest 来源、Gateway 可见性、Storage、RejectReport 和 QualityGate。
