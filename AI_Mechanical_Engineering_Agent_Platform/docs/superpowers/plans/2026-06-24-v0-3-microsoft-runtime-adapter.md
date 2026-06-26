# V0.3 Microsoft Runtime Adapter 实施计划归档

本文是 V0.3 的历史实施计划归档，保留用于追踪架构决策。

## 目标

在不污染平台契约的前提下，增加最小 Microsoft Agent Framework 适配边界。

## 架构约束

- `AgentRuntime.Microsoft` 是唯一允许引用 Microsoft Agent Framework 包的项目。
- 默认使用 `MockRuntime`。
- `PlatformCore`、Contracts、Schemas、`QualityGate` 和 Workers 不允许引用 Microsoft Runtime 类型。

## 技术栈

- C# / .NET 10
- `Microsoft.Agents.AI`
- xUnit

## 已完成任务

1. 增加 Runtime Adapter 测试，验证包引用隔离、契约不暴露 Microsoft 类型、`MicrosoftAgentAdapter` 实现 `IAgent`、MockRuntime 创建平台 Agent、Mock workflow 可运行、self-check 包含 V0.3 字段。
2. 增加 `AgentRuntimeMode`、`RuntimeAgentManifest`、`MockAgentRuntime`、`IMicrosoftRuntimeAgentInvoker`，并更新 `MicrosoftAgentAdapter` 和 `AgentFactory`。
3. 更新 `MicrosoftWorkflowRuntime` 和 `ToolBridge`，保留 tool-call 到 Skill / Worker 名称的映射骨架，但不执行真实 CAD。
4. 更新 `PlatformBootstrapper`、`SelfCheckReport`、`PlatformSelfCheckRunner` 和 Runtime README，确保 self-check 使用反射验证隔离。
