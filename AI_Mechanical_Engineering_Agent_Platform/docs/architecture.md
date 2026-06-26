# 架构说明

`AI_Mechanical_Engineering_Agent_Platform` 是面向机械工程自动化的长期可托管多 Agent 平台。当前重点是平台边界、运行时隔离、内部协作、质量门禁和可审计性，不接真实 CAD 软件。

## PlatformCore

`PlatformCore` 提供与具体 Agent Runtime 和 CAD 工具无关的平台能力：

- `TaskSystem`：任务生命周期和状态。
- `WorkflowEngine`：顺序工作流、步骤结果、Retry、FailureReport、HumanApprovalRequest。
- `AgentRegistry`、`SkillRegistry`、`ModuleRegistry`、`WorkerRegistry`：内存注册与查找。
- `ModuleManifestLoader`：读取 `module.yaml`，并记录 fallback 来源。
- `InternalAgentRouter`：只允许平台内部调用 Internal Agent，并记录审计日志。
- `ContextManager`：创建工作流上下文。
- `PermissionManager`：处理 Agent 可见性和 Gateway 暴露规则。
- `EventBus`：记录系统事件。
- `AuditLog`：记录任务、Agent、Skill、Worker 和 Gate 的执行日志。

## Contracts

`AgentContracts`、`ModuleContracts`、`SkillContracts` 和 `WorkerContracts` 定义平台稳定边界。

Agent 负责判断、协调和结构化输出。Skill 负责结构化转换和辅助能力。Worker 负责外部系统执行。Module 是完整能力板块，不是散乱脚本目录。

## DomainSchemas

`DomainSchemas` 存放机械和 CAD 任务之间传递的标准结构：

- `CADModelSpec`
- `BuildSpec`
- `DrawingSpec`
- `ReviewReport`
- `RejectReport`
- `GateDecision`
- `ArtifactInfo`
- `ErrorReport`
- `FinalReport`
- `InternalCollaborationReport`
- `FailureReport`
- `HumanApprovalRequest`
- `MarkdownLanguageReport`

核心任务状态必须通过这些 Schema 传递，不能只依赖自然语言。

## AgentRuntime.Microsoft

`AgentRuntime.Microsoft` 是唯一允许引用 Microsoft Agent Framework 相关包的项目。当前引用 `Microsoft.Agents.AI`，并默认使用 `MockRuntime`。

业务模块、Worker、`PlatformCore` 和 Contracts 不依赖 Microsoft Runtime API。真实 Runtime 只允许包装 `chief-engineer`，并且必须把模型输出转换为平台自己的 `AgentOutput`。

模型输出只提供任务理解和协作建议。最终内部调度仍由 `ChiefEngineerOrchestrator`、`SequentialWorkflowEngine`、Internal Agent workflow steps 和 `QualityGate` 控制。Internal Agents 在当前阶段继续使用 Module Agent 或 Mock Agent。

## Modules

每个 Module 都是完整能力板块，包含 agents、skills、workers、validators、reviewers、schemas 和 tests。

当前模块：

- `RequirementUnderstanding`
- `MechanicalDesign`
- `CADModeling`
- `DrawingGeneration`
- `DrawingReview`
- `CodeEngineering`
- `CodeReview`
- `ErrorDiagnosis`

Module 元数据优先从 `module.yaml` 加载。只有在 YAML 加载失败时才允许使用 fallback manifest，并必须写入审计。

## Workers

Worker 是未来调用 SolidWorks、AutoCAD、API、SDK、COM 或 MCP 工业软件桥接的执行层。当前只注册 Fake Worker：

- `FakeSolidWorksWorker`
- `FakeAutoCADWorker`

Agent 不允许直接调用 CAD API、COM 对象或外部进程。Agent 只能生成结构化计划并通过平台边界交给 Worker。

## QualityGate

`QualityGate` 负责校验、复审、打回、失败报告和人工审批挂起。它包含：

- Validator
- Reviewer
- Gatekeeper
- `GateDecisionPolicy`
- `RejectReportBuilder`
- `RetryPolicy`

`WorkflowEngine` 根据 `GateDecision` 做流程控制：`Passed` 进入下一步，`Rejected` 按策略重试或停止，`Failed` 生成 FailureReport，`NeedsHumanApproval` 生成 HumanApprovalRequest 并暂停。

## Interfaces

`Interfaces` 是平台入口层：

- `CliHost`：运行 self-check。
- `ApiHost`：预留 API Host。
- `AgentGatewayHost`：对外暴露 Public Agent Directory 和 Agent Message Endpoint。

Gateway 只暴露 Public Agent，当前只有 `chief-engineer`。Runtime、Internal Agent、Worker 和 QualityGate 的边界不会因为外部入口变化而改变。
