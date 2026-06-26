# 网关标准

`AgentGatewayHost` 是外部入口访问平台 Agent 的边界。OpenClaw、飞书、企业微信、Slack、Web 前端或其他入口只能通过 HTTP API 访问平台，不允许直接读取项目目录、调用 Internal Agent、调用 Worker、调用 Validator 或绕过 Gatekeeper。

当前允许的外部接口：

- `GET /agents`
- `POST /agents/{agentId}/message`

每个被接受的外部 Agent 消息都必须经过 Gateway 质量门禁链路：

1. 检查目标 Agent 是否存在。
2. 检查目标 Agent 是否为 `Public`。
3. 执行公开 Agent。
4. 必要时由 `chief-engineer` 通过 `WorkflowEngine` 调度 Internal Agent workflow steps。
5. 将 `AgentOutput` 和 `InternalCollaborationReport` 转换为 `ReviewReport`。
6. 通过 Gatekeeper 生成 `GateDecision`。
7. 只有 `GateDecision = Passed` 时才返回正常 Agent 输出。
8. 其他结果必须返回 `rejected`、`failed` 或 `needs_human_approval`，并附带门禁信息。

V0.7 及后续版本仍然只对外暴露 `chief-engineer`。`mechanical-designer`、`cad-modeler`、`drawing-engineer`、`drawing-reviewer`、`code-engineer`、`code-reviewer` 和 `error-diagnosis` 均为 `Internal`，不得通过 Gateway 直接调用。

外部可见 Agent 示例：

```json
[
  {
    "id": "chief-engineer",
    "display_name": "机械总工程师",
    "role": "总调度、任务拆解、内部 Agent 协作、质量裁决",
    "mention": "@机械总工程师",
    "visibility": "Public"
  }
]
```

`chief-engineer` 可以在配置允许时使用真实 Runtime 做任务理解，但 Runtime 输出只能作为建议。模型不能暴露 Internal Agent、不能调用 Worker、不能修改文件，也不能绕过 `WorkflowEngine` 或 `QualityGate`。

Gateway 响应可以包含 Runtime 元数据：

- `runtime_mode`
- `runtime_provider`
- `runtime_model`
- `runtime_fallback_used`
- `runtime_fallback_reason`
- `chief_engineer_runtime_used`

这些字段仅用于审计和排障，不会带来新的权限。
