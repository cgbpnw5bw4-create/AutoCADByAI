# 需求理解模块接口证据

## 目标与适用范围

本文件登记 [V2.2-A](../../../docs/v2_2_a_task_approval_lifecycle.md) 的宿主接口及纯平台流程合同。证据来自本仓库实现与后续行为验证，不涉及 SolidWorks COM 或其他真实 CAD API，不授予任何 CAD 执行能力。

## 输入与输出

输入为当前源码、接口合同与实际测试日志；输出为可定位的实现依据和验证状态。源码存在仅证明实现位置，不能代替测试通过或真实系统验收。

## 接口与证据来源

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

## 常见失败与禁止事项

发生身份不匹配或恢复缺失时按 [失败修复](failure_repair.md) 保留具体原因。禁止用源码字符串、宿主 `200` 或内存记录证明全局通过；禁止将本文件提升为 CAD API evidence、改写既有取证报告或降低既有能力基线。
