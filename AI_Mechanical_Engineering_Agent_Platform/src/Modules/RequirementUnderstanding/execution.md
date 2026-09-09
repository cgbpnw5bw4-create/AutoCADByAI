# 需求理解模块执行流程

## 目标与适用范围

本模块通过公开 `chief-engineer` 组织需求分析和内部 Agent 协作。[V2.2-A 阶段](../../../docs/v2_2_a_task_approval_lifecycle.md) 补齐任务审批恢复，使恢复与首次执行共用业务收尾及质量裁决；本文件覆盖该平台流程，不增加 CAD API 或上游结果类型交接。

## 输入与输出

输入为不可替换的原任务 `AgentContext`，恢复时另需绑定当前 `workflow_id`、`approval_request_id`、`step_id` 的明确审批决定。宿主负责校验任务访问令牌，提交者字段仅作审计。输出为 `AgentOutput`、协作报告、有效步骤结果、待审批请求或失败信息，随后由任务服务更新状态并执行宿主 `QualityGate`。

## 执行步骤

1. [ChiefEngineerAgent](agents/ChiefEngineerAgent.cs) 将首次请求交给 [ChiefEngineerOrchestrator](agents/ChiefEngineerOrchestrator.cs)，先拒绝无效显式 CAD 输入，再通过 `SequentialWorkflowEngine` 调用内部 Agent。
2. 等待人工审批时保存原任务上下文。恢复校验对应工作流，调用审批引擎原子匹配并消费具体审批；错误身份不执行下游。
3. 首次执行和恢复均调用 `CompleteWorkflowAsync`，生成累计协作报告，按实际审批裁决映射有效步骤输出。仅在内部流程通过且满足原有路由条件时进入既有 CAD 主流程，仍须遵守真实执行门禁。
4. 宿主任务服务读取最终 Agent 输出，经 `QualityGate` 更新任务状态。Microsoft Runtime 恢复复用该任务已保留的 advisory，不重新调用 LLM。

## 验证标准

按 [审查清单](review_checklist.md) 验证连续审批、错误身份、拒绝、后续失败、收尾路由与门禁，不启动 COM。构建、完整测试及自检由执行代理统一运行，实际结果登记阶段页；单进程内存状态不能作为持久恢复证据。

## 常见失败与禁止事项

出现上下文丢失、重复等待或最终状态不一致时，保留审计并按 [失败修复](failure_repair.md) 定位。禁止以引擎批准结果直接替代最终 Agent 输出，禁止重跑已完成步骤、跳过原业务收尾、直接调用 Worker、降低既有能力基线或把平台通过解释为真实 CAD 可交付。
