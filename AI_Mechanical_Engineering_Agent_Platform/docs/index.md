# 项目入口

## 项目定位

`AI Mechanical Engineering Agent Platform` 是面向机械设计、CAD 自动化、`SolidWorks`、`AutoCAD` 和后续工业软件适配的多 Agent 平台。平台目标不是堆脚本，而是把需求理解、建模计划、Worker 执行、校验、复审和质量门禁拆成长期可扩展的边界。

## 架构总览

- `Gateway`：外部入口，只能看到公开 Agent。
- `chief-engineer`：唯一公开 Runtime Agent，负责总调度和内部协作。
- `Internal Agents`：内部 Agent，例如 `cad-modeler`、`drawing-reviewer`，不能被外部直接调用。
- `Modules`：业务能力板块，位于 `src/Modules`。
- `Skills`：生成结构化计划或中间结果，不直接操作 CAD。
- `Workers`：真实执行层，未来调用 `API`、`SDK`、`COM` 或外部系统。
- `Validators`：检查输入、输出和环境。
- `Reviewers`：做工程合理性复审。
- `QualityGate`：统一裁决通过、打回、失败或人工审批。
- `AgentRuntime.Microsoft`：真实 LLM Runtime 适配层，不污染业务接口。
- `Storage`：任务、审计、事件、产物和报告抽象。
- `Interfaces`：CLI、API、AgentGatewayHost 等访问面。

## 三类 Agent 区分

- `Codex Agent` 位于 `.codex/agents`，用于开发协作，例如查代码、查 API、写文档或审查，不是产品运行时 Agent。
- `Runtime Agent` 位于项目代码中，例如 `chief-engineer`、`cad-modeler`，用于产品运行时任务处理。
- `Module` 位于 `src/Modules`，封装业务能力，不是聊天角色，也不是 Codex 子 Agent。

`Codex Agent Team` 不能替代 `src/Modules`，不能绕过 `Worker`、`Validator`、`Reviewer` 和 `QualityGate`。

## 当前开发入口

当前开发阶段为 [V2.2-A 平台任务生命周期与审批闭环](v2_2_a_task_approval_lifecycle.md)。前轮 V2.1-B 可靠性补强的 `R01`–`R05` 已完成；本轮在用户授权自主选择下一阶段后，实施 `R06` 的具体审批身份与原子防重放、任务状态及查询、宿主审批入口和恢复后的原业务收尾及 `QualityGate`。

上轮“不进入 V2.1-C”限定上轮范围，本轮 V2.2-A 平台开发已有新授权，不需要再次确认，也不扩大 CAD 功能。`R07` 类型交接与持久化留待后续；`R08`–`R11` 的 CAD 问题保持未解决及真实执行失败关闭，不阻塞本轮不依赖 COM 的平台开发。[历史架构审查](2026_09_09_architecture_review.md) 的结论继续保留。

宿主合同已登记：首次消息返回任务信息和仅返回一次的 `task_access_token`，后续 `GET /tasks/{taskId}`、`POST /tasks/{taskId}/approvals` 使用 `X-Task-Access-Token`。首次消息同步执行；任务与审批仅保存在单进程内存，恢复继续原后处理与门禁，Microsoft advisory 不重复调用 LLM。字段、状态码和验证记录见阶段页；`2.2-a-task-approval` 的四个新行为字段不代表完整测试或全局自检通过。

本入口面向开发与审查；输入为当前阶段、任务要求和源码证据，输出为适用协议、修复范围与验证记录。执行时先读下列必读文件，再按阶段说明开发、验证并回填结果。验证标准是源码、实际命令结果和报告一致；常见失败是引用历史报告代替当前证据，处理时须重新运行对应检查。禁止凭索引中的能力描述声称当前环境或真实 CAD 已通过。

## Codex 执行前必须读取

- `AGENTS.md`
- `docs/index.md`
- `docs/project_execution_standard.md`
- `docs/codex_execution_protocol.md`
- `docs/version_stage_index.md`
- `docs/v2_0_solidworks_default_on.md`
- `docs/v2_0_a_generic_cad_model_spec.md`
- 当前模块的 `execution.md`
- 当前模块的 `failure_repair.md`
- 当前模块的 `api_evidence.md`，如果涉及 API
- 当前模块的 `review_checklist.md`

## Claude 审查前必须读取

- `docs/claude_review_protocol.md`
- 当前模块的 `review_checklist.md`
- 当前阶段说明
- `output/reports/platform_self_check_report.json`
- 相关 `reviewrep` 审查日志

Claude 审查必须只读项目源码，审查报告只能写入 `../reviewrep`。
