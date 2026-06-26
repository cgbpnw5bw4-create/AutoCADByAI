# AgentRuntime.Microsoft

`AgentRuntime.Microsoft` 是本项目唯一允许引用 Microsoft Agent Framework 相关包的项目。

平台其他部分继续只使用自有契约：

- `IAgent`
- `AgentContext`
- `AgentOutput`
- `WorkflowStep`
- `WorkflowExecutionResult`
- `ISkill`
- `IWorker`

`AgentContracts`、`PlatformCore`、`ModuleContracts`、`SkillContracts`、`WorkerContracts`、`DomainSchemas`、`QualityGate` 和 CAD Worker 项目不得暴露 Microsoft Agent Framework 类型。

## Runtime 模式

当前默认 Runtime 模式是 `Mock`。

### MockRuntime

当没有配置真实模型 Provider、endpoint 或 API Key 时，self-check 和本地平台验证使用 Mock Runtime。

Mock Runtime 可以创建以下平台 Agent：

- `chief-engineer`，可见性为 `Public`。
- `mechanical-designer`，可见性为 `Internal`。
- `cad-modeler`，可见性为 `Internal`。
- `drawing-engineer`，可见性为 `Internal`。
- `drawing-reviewer`，可见性为 `Internal`。
- `code-engineer`，可见性为 `Internal`。
- `code-reviewer`，可见性为 `Internal`。
- `error-diagnosis`，可见性为 `Internal`。

Mock Runtime 返回确定性的 `AgentOutput`，不调用外部模型，也不调用 CAD 系统。

### MicrosoftRuntime

V0.7 及后续版本只允许 `chief-engineer` 使用真实 Runtime。

本项目引用 `Microsoft.Agents.AI`，但 Provider 凭据、模型名称和 endpoint 只能从环境变量读取：

- `AI_AGENT_RUNTIME_MODE`
- `AI_PROVIDER`
- `AI_MODEL`
- `AI_API_KEY`
- `AI_BASE_URL`
- `AI_TEMPERATURE`
- `AI_TIMEOUT_SECONDS`
- `AI_RUNTIME_STRICT_SMOKE_TEST`

`AI_AGENT_RUNTIME_MODE` 默认是 `Mock`。如果请求 Microsoft 模式但缺少 `AI_API_KEY`、`AI_PROVIDER` 或 `AI_MODEL`，Runtime 会自动 fallback 到 Mock，并记录 fallback 原因，但不会记录密钥。

`AI_TIMEOUT_SECONDS` 控制模型请求超时。默认值为 60 秒；非法值回退到默认值；小于 1 秒或大于 600 秒的值会被限制到边界范围。

当 `OpenAICompatibleModelClient` 自己创建 `HttpClient` 时，会把 `HttpClient.Timeout` 设置为 Runtime 配置值。当外部注入 `HttpClient` 时，不覆盖调用方已有 Timeout，但每次请求仍会使用由 `AI_TIMEOUT_SECONDS` 派生的 linked cancellation token。

Provider 错误会转换为结构化 Runtime issue：

- HTTP 401 / 403 转换为 `auth_error`。
- HTTP 429 转换为 `rate_limit`。
- HTTP 5xx 和其他非成功 Provider 响应转换为 `provider_error`。
- 请求超时转换为 `timeout`。
- OpenAI-compatible JSON 格式不合法转换为 `invalid_provider_response`。
- 传输层异常转换为 `network_error`。

API Key 不得写入代码、`appsettings.json`、AuditLog、Gateway 响应或 Runtime failure issue。

真实 `chief-engineer` Runtime 只负责理解请求并给出协作建议。该建议不是权威执行指令。包装后的 `chief-engineer` 仍会运行平台 `ChiefEngineerOrchestrator`，并通过 `SequentialWorkflowEngine`、Internal Agent workflow steps 和 `QualityGate` 完成内部协作。

## 组件

- `MicrosoftAgentAdapter`：把 Runtime 行为包装为平台 `IAgent`。
- `AgentFactory`：创建 Mock Agent，并且只允许包装 `chief-engineer` 为 Microsoft Runtime Agent。
- `MicrosoftRuntimeAgentInvoker`：调用 Runtime 模型，并把输出映射为平台 `AgentOutput`。
- `MicrosoftAgentOutputMapper`：把 JSON 或纯文本模型输出转换为平台 `AgentOutput`，并阻止权限提升或直接 Worker 调用。
- `RuntimeConfiguration`：从环境变量读取 Runtime 模式和 Provider 配置。
- `IRuntimeModelClient`：Runtime 内部 Provider 抽象，用于 Microsoft Agent Framework 和 OpenAI-compatible fallback。
- `OpenAICompatibleModelClient`：执行 OpenAI-compatible chat completion 请求，并提供 timeout、cancellation 和结构化 Provider 错误。
- `MicrosoftWorkflowRuntime`：使用平台 workflow contract 包装顺序工作流执行。
- `ToolBridge`：把未来 Runtime tool call 映射为平台 Skill 或 Worker 名称，并转换输出消息。

## 边界

Agent 不得直接调用 CAD Worker。CAD 执行必须继续经过平台 `WorkerContracts`、Module 边界和 `QualityGate`。

`ToolBridge` 只是映射层，不执行 SolidWorks、AutoCAD、COM、SDK、API 或 MCP 调用。

真实 LLM 输出不得：

- 直接调用 Worker。
- 修改文件。
- 改变 Agent 可见性。
- 通过 Gateway 暴露 Internal Agent。
- 跳过 `WorkflowEngine`。
- 跳过 `QualityGate`。

Runtime 失败也不能绕过 `QualityGate`。如果真实 Runtime 调用失败，失败会以平台 `AgentOutput` issue 返回，并继续由 Gateway 和工作流门禁处理。
