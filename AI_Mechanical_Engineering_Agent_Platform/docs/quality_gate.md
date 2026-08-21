# 质量门禁

`QualityGate` 用于把执行与复审分开。它不负责调用 CAD，也不负责生成模型，而是负责判断结果是否可以继续进入下一步。

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

## 人工审批提交与恢复

当步骤返回 `NeedsHumanApproval` 时，`SequentialWorkflowEngine` 会保存等待步骤、已完成步骤、剩余步骤、请求和审计片段到 `IWorkflowApprovalStore`。外部宿主必须显式调用 `SubmitHumanApprovalAsync` 提交 `Approve`、`Reject` 或 `RequestRevision`，工作流不会仅因等待而自动恢复。

- `Approve` 将等待步骤转为 `Passed`，再由同一 `WorkflowEngine` 执行未运行的后续步骤。
- `Reject` 与 `RequestRevision` 立即停止下游，并生成可追踪的 `FailureReport`。
- 非法决定或不存在的工作流不会消耗待审批项。
- 默认 `InMemoryWorkflowApprovalStore` 只保证单进程宿主内的提交/恢复闭环；需要跨进程或跨重启恢复时，宿主必须注入满足同一接口的持久化存储，不能把内存状态伪装为已持久化。
