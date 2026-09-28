# AgentRuntime.Microsoft

## 目标、适用范围与输入输出

`AgentRuntime.Microsoft` 是 `AI Mechanical Engineer Platform` 保留的兼容模型适配项目，仍是唯一允许引用 Microsoft Agent Framework 相关包的项目。其名称不代表业务必须绑定特定模型。输入为平台 Agent 上下文与接入配置；输出为经过映射和权限约束的 `AgentOutput`，不输出可直接执行的 CAD 指令。

统一模型合同位于独立 `src/ModelRuntime`，没有供应商 SDK 依赖。`IModelProvider.GenerateAsync(ModelRequest, CancellationToken)` 只接受工程用途、系统提示和用户消息，返回待校验文本；`ModelRuntime.GenerateAsync` 拒绝未知用途、空输入和空结果，并传递取消。

`EngineeringModelPurpose` 仅包含需求理解、设计规划、特征规划、建模顺序、装配策略、恢复建议和工程决策。它不包含执行 Worker、调用 CAD API 或授予 API evidence 的用途。

平台其他部分继续只使用自有契约：

- `IAgent`
- `AgentContext`
- `AgentOutput`
- `WorkflowStep`
- `WorkflowExecutionResult`
- `ISkill`
- `IWorker`
- `IModelProvider`
- `ModelRequest`
- `IWorkflowEngine`

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

当前只允许 `chief-engineer` 使用真实 Runtime。旧模式名 `Microsoft` 和配置键为兼容入口保留；并未将具体模型名称写入业务合同。

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

真实 `chief-engineer` Runtime 负责工程理解、规划和决策建议。该建议不是执行授权。包装后的 `chief-engineer` 仍会运行平台 `ChiefEngineerOrchestrator`，通过 `IWorkflowEngine` 和 `QualityGate` 完成受控业务流程，默认实现保留 `SequentialWorkflowEngine` 的顺序、重试与审批语义。

平台默认路线以 `EngineeringPlanValidationStep` 校验输入，不再固定调度四个占位内部角色，也不生成工程计划；原四角色路线可通过 `InternalWorkflowRoute.EngineeringDefault` 显式选择。真实模型输出与平台输入校验属于不同职责，后者通过不意味着模型或 CAD 结果已验收。

## 组件

- `MicrosoftAgentAdapter`：把 Runtime 行为包装为平台 `IAgent`。
- `AgentFactory`：创建 Mock Agent，并且只允许包装 `chief-engineer` 为 Microsoft Runtime Agent。
- `MicrosoftRuntimeAgentInvoker`：调用 Runtime 模型，并把输出映射为平台 `AgentOutput`。
- `MicrosoftAgentOutputMapper`：把 JSON 或纯文本模型输出转换为平台 `AgentOutput`，并阻止权限提升或直接 Worker 调用。
- `RuntimeConfiguration`：从环境变量读取 Runtime 模式和 Provider 配置。
- `ModelRuntime` / `IModelProvider`：中立模型入口与供应方合同，不携带 Worker、Registry 或 COM 对象。
- `ConfiguredModelProvider`：将既有 `IRuntimeModelClient` 和 `RuntimeConfiguration` 适配为中立供应方，旧客户端与配置继续兼容。
- `IRuntimeModelClient`：兼容适配层内部客户端合同，用于 Microsoft Agent Framework 和 OpenAI-compatible fallback，不再作为业务层统一模型合同。
- `OpenAICompatibleModelClient`：执行 OpenAI-compatible chat completion 请求，并提供 timeout、cancellation 和结构化 Provider 错误。
- `MicrosoftWorkflowRuntime`：使用平台 workflow contract 包装顺序工作流执行。
- `ToolBridge`：保留 Skill 调用、工具名称映射和输出消息转换；不再持有 Worker Registry 或提供直接 Worker 调用方法。

`AgentFactory.CreateRuntimeAwareAgent` 和 `RuntimePlatformFactory.ApplyRuntimeConfiguration` 可以注入 `IModelProvider`；不注入时使用现有配置创建 `ConfiguredModelProvider`。现有宿主接入仍保留 `RuntimeConfiguration.EffectiveMode` 的模式判定和 Mock 回退，不等于本轮已实现全部供应商的原生客户端或新宿主配置体系。

重复配置先解包底层 Agent，供应方替换不会叠加 Runtime 包装而重复调用；切回 Mock 恢复原 Agent。待审批时禁止重配，保留原模型建议和恢复链。当前支持空闲时配置替换，不承诺并发热切换或跨重启恢复。

## 边界

Agent 不得直接调用 CAD Worker。CAD 执行必须继续经过平台 `WorkerContracts`、Module 边界和 `QualityGate`。

`ToolBridge.InvokeWorkerAsync` 已删除，旧双参数构造函数仅保留源代码兼容，不保存或使用 Worker Registry。工具名称映射不授予执行权限；所有正式 CAD 能力仍遵循 `Feature Registry → Verified Handler → SolidWorks API Evidence → Worker → SolidWorks Adapter`。

真实 LLM 输出不得：

- 直接调用 Worker。
- 修改文件。
- 改变 Agent 可见性。
- 通过 Gateway 暴露 Internal Agent。
- 跳过 `WorkflowEngine`。
- 跳过 `QualityGate`。

Runtime 失败也不能绕过 `QualityGate`。如果真实 Runtime 调用失败，失败会以平台 `AgentOutput` issue 返回，并继续由 Gateway 和工作流门禁处理。

## 执行步骤、验证标准与常见失败

先按既有配置选择 Mock 或真实兼容入口，再由中立 ModelRuntime 调用供应方、映射建议并进入平台工作流。新增供应方实现 `IModelProvider` 并注入工厂即可复用业务合同；供应方专有设置留在接入层，不能传入 Worker 或提升 Agent 可见性。

验证应覆盖不同供应方替身、请求用途、取消、空结果、旧客户端兼容、工厂注入和无直接 Worker 执行入口。构建、完整测试和自检统一由执行代理运行，实际结果见 [V2.2-B 阶段页](../../docs/v2_2_b_frontier_model_architecture.md)。配置缺失时遵循既有 Mock 回退，供应方错误保留结构化 issue，禁止记录密钥、假装调用成功或把 Mock 结果当作真实模型及 CAD 验证。
