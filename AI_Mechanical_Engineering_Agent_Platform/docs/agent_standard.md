# Agent 标准

Agent 不是聊天人设，而是具备职责、权限、输入输出和质量边界的执行角色。

每个 Agent 必须具备：

- 稳定的 `Id`
- 展示名称 `Name`
- 角色 `Role`
- 描述 `Description`
- 可见性 `Visibility`
- 结构化 `AgentInput`
- 结构化 `AgentOutput`
- 审计日志
- 必要时给出 `NextRecommendedAgentId`

## 可见性规则

- `Public`：可以通过 `AgentGatewayHost` 暴露给外部入口。
- `Internal`：只能由平台内部流程调用。
- `Protected`：只能由系统流程调用，例如 Gatekeeper 或 Validator。

Agent 负责判断、协调和输出结构化结果。Agent 不直接操作 CAD 软件，也不直接调用 SolidWorks、AutoCAD、COM、SDK 或 Worker。CAD 执行属于 Worker。

## Runtime 支持的 Agent

V0.7 及后续版本只允许 `chief-engineer` 使用真实 Microsoft Runtime / LLM 集成。

Runtime 输出必须转换为平台 `AgentOutput`。模型可以理解任务并提出内部协作建议，但不能：

- 改变 Agent 可见性。
- 通过 Gateway 暴露 Internal Agent。
- 直接调用 Worker。
- 操作 SolidWorks 或 AutoCAD。
- 修改文件。
- 绕过 `WorkflowEngine`。
- 绕过 `QualityGate`。

Internal Agents 在后续版本明确升级前，继续使用 Module Agent 或 Mock Agent 实现。
