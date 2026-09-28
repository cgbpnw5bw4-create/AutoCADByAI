# Agent 标准

## 目标、适用范围与输入输出

`AI Mechanical Engineer Platform` 的 Agent 是具备职责、权限、输入输出和质量边界的工程角色。本规范适用于产品运行时 Agent；开发协作的 Codex Agent 按独立注册表管理。输入为需求、约束、工程上下文和能力描述；输出为结构化工程理解、计划、决策与恢复建议。

优先使用少量强 Agent 与多个确定性 Worker。需要独立工程判断时才增加 Agent；结构化转换、能力查询、重复流程和外部执行优先复用 Skill、Registry、WorkflowEngine 或 Worker。

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

Agent 负责需求理解、设计规划、特征规划、建模顺序、装配策略、异常恢复建议和工程决策。Agent 不直接操作 CAD 软件，也不直接调用 SolidWorks、AutoCAD、COM、SDK 或 Worker。CAD 执行属于确定性 Worker，执行权限不由模型输出授予。

## Runtime 支持的 Agent

当前只允许公开 `chief-engineer` 使用真实模型集成。业务层通过 `ModelRuntime` / `IModelProvider` 接入模型；既有 `AgentRuntime.Microsoft` 保留为兼容接入实现，具体供应方和模型名称仅由接入配置决定。

Runtime 输出必须转换为平台 `AgentOutput`。模型可以形成工程计划和决策建议，但不能：

- 改变 Agent 可见性。
- 通过 Gateway 暴露 Internal Agent。
- 直接调用 Worker。
- 操作 SolidWorks 或 AutoCAD。
- 修改文件。
- 绕过 `WorkflowEngine`。
- 绕过 `QualityGate`。
- 把自己生成的 API 名称、参数或说明当作已验证 SolidWorks 能力。

已有 Internal Agents 继续保留 Module Agent 或 Mock Agent 兼容实现。它们是确定性实现或占位角色，不应描述为每个角色都运行独立强模型；默认路线的收敛不等于删除所有历史角色。

默认工程路线现在仅以 `EngineeringPlanValidationStep` 做规划输入校验，省去四个占位角色的固定调度。此步骤不生成工程计划、不调用模型或 Worker；具体 CAD 计划仍由原 Skill/Validator 负责。需要既有四角色流程的宿主或回归可以显式注入 `InternalWorkflowRoute.EngineeringDefault`，继续使用其审批和重试语义。

## 执行步骤

1. 由公开工程 Agent 通过 ModelRuntime 理解需求并形成结构化计划，复用 Skill 和能力 Registry。
2. 平台工作流校验计划、参数和能力来源；缺少实现或证据时返回可行动问题。
3. 正式执行必须遵守 `Feature Registry → Verified Handler → SolidWorks API Evidence → Worker → SolidWorks Adapter`，Handler 不直接访问 COM。
4. Worker 输出进入 Validator、Reviewer 与 QualityGate；需要人工决定时按具体审批身份挂起和恢复。

## 验证标准、常见失败与禁止事项

新增模型供应方应通过相同合同运行替身回归，确认业务不依赖模型名、取消和错误可追踪、模型无法获得 Worker 执行权限。新增 Agent 应说明独立决策价值，并验证公开可见性与调用链。完整测试和全局自检的实际结果见当前阶段页。

若模型返回越权字段、未知能力或无效计划，保留问题并停止对应执行，不能转成默认 CAD 请求。禁止为纯消息转发新增 Agent、复制多层 JSON 作为内部状态、自动采纳模型 API 建议或把未实现的装配策略描述为真实装配能力。
