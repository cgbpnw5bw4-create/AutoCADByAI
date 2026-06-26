# V0.2 Internal Agent Routing 实施计划归档

本文是 V0.2 的历史实施计划归档。

## 目标

让公开的 `chief-engineer` 可以在平台内部调度 Internal Agents，同时 Gateway 仍然只暴露 `chief-engineer`。

## 架构约束

- Gateway 是唯一外部访问边界。
- `InternalAgentRouter` 只服务平台内部调用。
- `ChiefEngineerOrchestrator` 负责固定的内部协作链路。
- `InternalCollaborationReport` 是协作结果进入 `QualityGate` 的结构化载体。

## 已完成任务

1. 新增 Internal Routing 测试，覆盖顺序调用、协作报告、Gateway response、Gateway 阻断 Internal Agent、self-check 字段。
2. 新增 `InternalCollaborationReport` 和 `InternalAgentRouter`，并扩展 `AgentOutput`。
3. 创建 `ChiefEngineerOrchestrator`，让 `chief-engineer` 调度 `mechanical-designer`、`cad-modeler`、`drawing-engineer` 和 `drawing-reviewer`。
4. 更新 Gateway、`AgentOutputReviewMapper`、self-check 和审计日志，确保协作结果通过 `QualityGate`。
