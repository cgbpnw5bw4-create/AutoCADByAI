# 需求理解模块失败修复

## 目标与适用范围

本手册用于 [V2.2-A 任务审批闭环](../../../docs/v2_2_a_task_approval_lifecycle.md) 的需求理解与 `chief-engineer` 首次执行、等待、恢复及收尾失败。目标是保留任务身份与历史，按直接原因做最小修复。

## 输入与输出

输入为原请求、任务状态、当前审批身份、`failure_stage` 或 `failure_reason`、协作报告和审计；令牌仅用于授权查询，不写入诊断说明。输出必须包含失败阶段、直接原因、证据路径、修复策略和下一步验证命令。

## 执行步骤

| 失败或现象 | 查证与修复 |
|---|---|
| `chief_approval_context_not_pending` | 核对原任务与 `internal-collaboration-{taskId}`，检查是否已终止或进程已重启；不得创建空上下文冒充恢复 |
| `workflow_approval_identity_mismatch` 或 `workflow_approval_not_pending_or_mismatched` | 对比本次等待的三个标识；保留当前有效审批，拒绝旧提交，不自动改身份后批准 |
| `task_approval_in_progress` 或 `task_approval_not_pending` | 凭任务令牌查询现状；已消费审批不能重放，执行中等待其真实结果 |
| `runtime_approval_advisory_missing` | 检查同任务 advisory 是否保留及 Runtime 模式；不得为恢复而重新调用 LLM 生成替代内容 |
| 恢复后再次异常等待、假成功或遗漏收尾 | 核对 `CompleteWorkflowAsync`、有效审批结果映射、原 CAD 路由条件及宿主 `QualityGate`，保留前后结果定位最小分支 |
| `task_execution_exception` 或 `task_execution_cancelled` | 保留异常类型、任务终态和审计；HTTP 断开不等于已接受动作取消，不能据此重复执行 |

## 验证标准

修复后在不依赖 COM 的夹具中重现原反例，检查审批项是否被正确消费、下游执行次数、累计报告、任务终态和质量门禁。执行代理从项目根目录运行 `dotnet test`，再运行 `dotnet run --project src/Interfaces/CliHost -- self-check --output output/validation/v2_2_a_task_approval`，在阶段页记录实际结果；命令未完成时不得写通过。

## 常见失败与禁止事项

常见误修是把中间批准映射成整个任务完成、丢弃上下文后重建任务或将历史审批改成当前身份。禁止这些做法；禁止输出访问令牌、改旧 evidence、降低既有能力基线。遇到真实 CAD 证据失败，转到对应 Worker 手册，保留本轮平台结果与 CAD 失败的区别。
