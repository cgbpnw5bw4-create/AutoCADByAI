# 需求理解模块接口证据

## 目标与适用范围

本文件登记 [V2.3](../../../docs/v2_3_engineering_planning_loop.md) 的模型规划、校验与既有 CAD 合同交接，以及 [V2.2-A](../../../docs/v2_2_a_task_approval_lifecycle.md) 的宿主接口和纯平台流程。证据来自本仓库实现与行为验证；这些平台接口不授予 SolidWorks COM 或其他真实 CAD API 执行能力，真实执行继续使用原 Worker evidence。

## 输入与输出

输入为当前源码、接口合同与实际测试日志；输出为可定位的实现依据和验证状态。源码存在仅证明实现位置，不能代替测试通过或真实系统验收。

## V2.3 合同与证据来源

| 合同 | 实现来源 | 证据边界 |
|---|---|---|
| 工程计划与既有 CAD 类型复用 | [EngineeringPlan.cs](../../DomainSchemas/EngineeringPlan.cs) | 八字段规划合同内嵌现有 `CADModelSpec`；没有第二套 CAD Schema，没有 Worker 权限 |
| 模型仅理解和规划 | [EngineeringPlanningStep.cs](../../PlatformCore/EngineeringPlanningStep.cs) | 经现有 `ModelRuntime` / `IModelProvider` 使用 `DesignPlanning`；提示和用途限制不构成真实 CAD 证据 |
| 严格解析先于兼容转换 | [EngineeringPlanParser.cs](../../PlatformCore/EngineeringPlanParser.cs) | 原 JSON 字段、重复项、类型、深度和长度检查；未知字段不能被转换器静默接受 |
| 工程规则和原文校验 | [EngineeringPlanValidator.cs](../../PlatformCore/EngineeringPlanValidator.cs) | 只接纳140/120/180 mm受证夹套参数，检查原文、假设/缺参/风险及未知附加要求；不是任意需求理解能力 |
| 既有模板与编译链 | 同一 Validator 内的 `PartFamilyGenericModelFactory`、既有 `CADModelSpecValidator`、`SolidWorksBuildPlanValidator` 和 `SolidWorksBuildPlanReviewer` | 模型只填参数投影，图由平台产生；最终 Worker evidence、几何和 QualityGate 仍独立裁决 |
| 专用公开路线与安全交接 | [ChiefEngineerOrchestrator.cs](agents/ChiefEngineerOrchestrator.cs) 与规划步骤 `ToCadContext` | 仅显式 `engineering_planning=true` 选择，计划门禁通过后进入原 CAD 主流程；失败不回退默认零件 |
| CLI 响应来源与结果报告 | [Program.cs](../../Interfaces/CliHost/Program.cs) 的 `run-engineering-plan` | `--response` 为显式固定响应重放；`plan_source`、dry-run和真实执行分别记录 |
| 工程规划行为自检 | [EngineeringPlanningSelfCheck.cs](../../PlatformCore/EngineeringPlanningSelfCheck.cs) | 五项字典检查使用替身和 dry-run，不证明真实模型稳定性或当前 CAD 交付 |

已有真实夹套 evidence 的生产准入来源保持 [V2.2-D 阶段记录](../../../docs/v2_2_d_shaft_jacket_real_baseline.md) 和原 CAD / Worker 接口证据。模型响应、`rationale`、规划门禁或本文件的实现说明不能替代这些来源，不把固定响应重放当作真实模型证据。

## V2.2-A 兼容接口与证据来源

| 合同 | 实现来源 | 证据边界 |
|---|---|---|
| 首次与恢复共用业务收尾 | [ChiefEngineerOrchestrator.cs](agents/ChiefEngineerOrchestrator.cs) 的 `ExecuteAsync`、`ResumeHumanApprovalAsync`、`CompleteWorkflowAsync` | 原上下文恢复、报告累计及原路由后处理；行为结果见阶段页最终记录 |
| 具体审批身份原子消费 | [WorkflowApprovalStore.cs](../../PlatformCore/WorkflowEngine/WorkflowApprovalStore.cs) 的 `TryTake(workflowId, approvalRequestId, stepId, out pending)` | 单进程原子匹配，不是跨重启持久化 |
| 宿主查询与审批、访问令牌 | [Program.cs](../../Interfaces/AgentGatewayHost/Program.cs)、[AgentTaskService.cs](../../PlatformCore/TaskSystem/AgentTaskService.cs) | `GET /tasks/{taskId}`、`POST /tasks/{taskId}/approvals` 要求 `X-Task-Access-Token`；令牌不是用户账户身份 |
| Microsoft advisory 保留 | [MicrosoftAgentAdapter.cs](../../AgentRuntime.Microsoft/MicrosoftAgentAdapter.cs) 的 `ResumeHumanApprovalAsync` | 同任务恢复复用原 advisory，不再次调用 LLM；内存边界仍有效 |

## 执行步骤与验证标准

1. 修改接口前读取 [执行流程](execution.md) 与 [审查清单](review_checklist.md)，核对调用方和状态含义。
2. 用错误身份、连续审批、并发提交、下游失败和 Runtime 恢复反例验证真实行为；测试位置包括 `GatewayTaskLifecycleTests`、`WorkflowApprovalIdentityTests`、`ApprovalCadContinuationTests` 和 `AgentRuntimeMicrosoftTests`。
3. 将实际命令、计数、日志及四个阶段行为字段回填阶段页。完整测试与全局自检另列，本文件的源码说明不替代阶段页中的实际执行记录。

V2.3 还须逐项查证模型合同调用、严格拒绝、模板映射、公开路线、缺 Provider 失败关闭和原质量链的实际行为。完整验证由执行代理统一运行，五项 `engineering_planning_checks` 与构建、完整测试、全局自检、真实 CAD 重开及实时模型稳定性分别记录；未配置真实 Provider 时该模型稳定性项目保留未完成，不能用替身或重放填通过。

## 常见失败与禁止事项

发生身份不匹配或恢复缺失时按 [失败修复](failure_repair.md) 保留具体原因。禁止用源码字符串、宿主 `200` 或内存记录证明全局通过；禁止将本文件提升为 CAD API evidence、改写既有取证报告或降低既有能力基线。

计划拒绝按新增专用失败原因处理；禁止输出提供商凭据、直接调用 CAD API、把未知或附加要求吞掉、另建 CAD Schema、修改受证源码或通过审批绕过计划/证据门禁。
