# 质量门禁

`QualityGate` 用于把执行与复审分开。它不负责调用 CAD，也不负责生成模型，而是负责判断结果是否可以继续进入下一步。

## 目标、适用范围与输入输出

本规范适用于工作流步骤的校验、复审、重试和人工审批。输入为步骤结果、`ReviewReport`、重试策略及显式审批提交；输出为 `GateDecision`、完整步骤历史、审计、失败报告或待审批请求。目标是让真实执行次数、状态转换和可追踪证据保持一致。

核心组件：

- Validator：确定性校验，生成或辅助生成 `ReviewReport`。
- Reviewer：复审逻辑，生成 `ReviewReport`。
- Gatekeeper：读取 `ReviewReport` 并生成 `GateDecision`。
- `RejectReportBuilder`：把 rejected review 转换为 `RejectReport`。
- `RetryPolicy`：定义重试、退避和升级行为。

`GateDecision` 结果：

- `Passed`
- `Rejected`
- `Failed`
- `NeedsHumanApproval`

基础流程：

1. Worker 或 Skill 输出被复审。
2. Validator 或 Reviewer 创建 `ReviewReport`。
3. Gatekeeper 应用 `GateDecisionPolicy`。
4. 如果被打回，`RejectReportBuilder` 创建 `RejectReport`。
5. `WorkflowEngine` 根据裁决继续、重试、失败或等待人工审批。

## Retry 延迟语义

`RetryPolicy` 不只决定是否可以重试，还通过 `GetDelay(retryCount)` 决定每次重试前的等待时间。

- `DefaultRetryPolicy` 可以返回 `TimeSpan.Zero`，表示立即重试。
- `ExponentialBackoffRetryPolicy` 根据 `BaseDelayMs`、`Multiplier` 和 `MaxDelayMs` 计算递增延迟。
- `SequentialWorkflowEngine` 遇到正数延迟时必须实际 `await Task.Delay(...)`。
- retry delay 必须支持 `CancellationToken`，以便工作流在等待期间可取消。
- retry delay 必须写入 AuditLog。

延迟只适用于可重试的 `Rejected` step。`Failed` 和 `NeedsHumanApproval` 是自动流程的终止状态，不能等待 retry delay，也不能自动重试。

有效重试上限统一为 `min(step.MaxRetries ?? global.MaxRetries, global.MaxRetries)`。步骤可以降低全局上限，不能扩大它；局部未设置时使用全局上限。`MaxRetries=0` 允许首次执行，但首次被打回后不再尝试。是否重试必须同时满足有效上限与 `IRetryPolicy.ShouldRetry`；结果中的 `MaxRetries`、审计中声明的次数和实际执行次数必须相同。每次重试前仍按上述延迟语义等待。

## 人工审批提交与恢复

当步骤返回 `NeedsHumanApproval` 时，`SequentialWorkflowEngine` 会保存等待步骤、已完成步骤、剩余步骤、请求和审计片段到 `IWorkflowApprovalStore`。外部宿主必须显式调用 `SubmitHumanApprovalAsync` 提交 `Approve`、`Reject` 或 `RequestRevision`，工作流不会仅因等待而自动恢复。

- `Approve` 将等待步骤转为 `Passed`，再由同一 `WorkflowEngine` 执行未运行的后续步骤。
- `Reject` 与 `RequestRevision` 立即停止下游，并生成可追踪的 `FailureReport`。
- 非法决定或不存在的工作流不会消耗待审批项。
- 提交时已取消的 `CancellationToken` 必须在消费待审批项之前被检查；预取消不得改变待审批请求、已完成步骤和审计，之后有效提交仍可继续处理。
- 多次审批必须累计保存已完成步骤与全部历史审计。恢复后再次等待时，存储内容必须包含第一次暂停前的结果、第一次审批决定与本次恢复段；最终批准、拒绝或要求修订不得丢失前段历史。
- 返回结果与待审批存储都必须包含本次提交及决定审计；同一事件不得因历史拼接而重复。审批审计应能够关联提交者、时间、决定及对应步骤。
- 默认 `InMemoryWorkflowApprovalStore` 只保证单进程宿主内的提交/恢复闭环；需要跨进程或跨重启恢复时，宿主必须注入满足同一接口的持久化存储，不能把内存状态伪装为已持久化。

### V2.1-B 历史限制

V2.1-B 可靠性补强结束时，无损取消承诺仅覆盖提交前已取消的情况；当时的审批提交合同尚未绑定具体步骤身份，跨审批重放保护仍待开发。以上是历史阶段边界，不再用于描述 V2.2-A 的当前身份合同。执行中取消后的跨重启持久恢复仍未完成。

### V2.2-A 当前合同

当前按 [V2.2-A 平台任务生命周期与审批闭环](v2_2_a_task_approval_lifecycle.md) 执行：审批绑定 `workflow_id`、`approval_request_id`、`step_id`，存储原子匹配后才消费；错误身份、重复提交及旧审批作用于新等待均被拒绝。宿主查询与提交要求任务访问令牌，审批恢复复用 `chief-engineer` 原后处理并重新进入统一 `QualityGate`，批准节点不等于任务最终通过。

任务、审批和 Microsoft advisory 仅保存在单进程内存中；恢复复用原 advisory，不再次调用 LLM。审批接受后 HTTP 断开不取消已接受动作，调用方凭已有令牌查询结果；重启后的任务恢复与持久化仍未实现。访问令牌是任务范围访问能力，`submitted_by` 仅用于审计，不代表用户账户认证。

## 验证标准、常见失败与禁止事项

验证先构造步骤局部上限低于、高于及未设置全局上限的打回场景，再检查真实调用次数、最终状态和报告上限。审批回归至少包含预取消后重新提交、连续两次审批、批准后拒绝、要求修订及最终无后续步骤；同时检查待审批项、完整步骤顺序和每类审计事件。

出现次数超限、待审批项意外消失或历史缺段时，先保留失败结果，定位重试判据、消费时点或恢复快照，按 [架构审查清单](2026_09_09_architecture_review.md) 做最小修复后重跑对应反例。禁止通过改报告上限掩盖过量执行、自动批准等待项、丢弃失败步骤或把内存恢复宣称为持久化完成。

V2.1-B 新增的 `workflow_step_retry_limit_enforced`、`workflow_cancelled_approval_preserved`、`workflow_multi_approval_history_preserved` 只覆盖当时的重试、预取消与历史累计检查，不是当前审批身份保护的依据。V2.2-A 通过 `workflow_approval_identity_bound` 及任务生命周期、审批闭环、令牌检查表达新增行为；具体字段与验证结果见当前阶段页，仍不包含跨重启持久化。

实际完整测试与全局质量门禁另行裁决；已有 CAD 文件与报告 `size_bytes` 不一致时，物理完整性检查必须失败，禁止通过改长度或 hash 绕过。能力基线可以追加经验证的保护项，禁止降低既有能力基线以掩盖回归。
