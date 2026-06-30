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

## Codex 执行前必须读取

- `AGENTS.md`
- `docs/index.md`
- `docs/project_execution_standard.md`
- `docs/codex_execution_protocol.md`
- `docs/version_stage_index.md`
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
