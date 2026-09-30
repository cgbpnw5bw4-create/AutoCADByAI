# 项目入口

当前开发入口为 [V2.3 工程规划闭环](v2_3_engineering_planning_loop.md)。本轮复用 `ModelRuntime` / `IModelProvider`，以已受证的140/120/180 mm夹套验证自然语言、工程计划、现有 CAD 合同和真实质量链的交接；不扩展产品运行时多 Agent、并行或通用工作流语言。下述 V2.2-C 统计属于历史阶段，本轮实际验证单独登记。


## 项目定位

`AI Mechanical Engineer Platform` 是面向机械设计、CAD 自动化、`SolidWorks`、`AutoCAD` 和后续工业软件适配的平台。强模型负责工程理解、规划和决策；平台负责工程约束、确定性执行、真实 CAD/API 能力和结果验收。架构逐步收敛为少量强 Agent 与多个确定性 Worker。

正式名称、快速入口和底层命名兼容原因见 [项目 README](../README.md)。旧项目目录、解决方案、程序集、命名空间和来源绑定证据路径继续保留。

## 架构总览

- `Gateway`：外部入口，只能看到公开 Agent。
- `chief-engineer`：唯一公开 Runtime Agent，负责工程理解、规划和决策。
- `Internal Agents`：保留兼容的内部角色，例如 `cad-modeler`、`drawing-reviewer`，按需求调用，不能被外部直接调用；确定性能力不要求拆成多个推理 Agent。
- `Modules`：业务能力板块，位于 `src/Modules`。
- `Skills`：生成结构化计划或中间结果，不直接操作 CAD。
- `Workers`：确定性执行层，通过受证能力和 Adapter 调用 `API`、`SDK`、`COM` 或外部系统。
- `Feature Registry`、Verified Handler 与 `SolidWorks API Evidence`：负责能力发现、参数校验和真实执行授权；模型 API 建议不构成证据。
- `Validators`：检查输入、输出和环境。
- `Reviewers`：做工程合理性复审。
- `QualityGate`：工程结果验收，统一裁决通过、打回、失败或人工审批，为专项门禁保留可组合边界。
- `ModelRuntime` / `IModelProvider`：中立模型接入和替换；`AgentRuntime.Microsoft` 保留为兼容适配层。
- `IWorkflowEngine`：统一工作流调用合同，当前实现仍为已验证的顺序引擎，保留重试与人工审批。
- `Storage`：任务、审计、事件、产物和报告抽象。
- `Interfaces`：CLI、API、AgentGatewayHost 等访问面。

## 三类 Agent 区分

- `Codex Agent` 位于 `.codex/agents`，用于开发协作，例如查代码、查 API、写文档或审查，不是产品运行时 Agent。
- `Runtime Agent` 位于项目代码中，例如 `chief-engineer`、`cad-modeler`，用于产品运行时任务处理。
- `Module` 位于 `src/Modules`，封装业务能力，不是聊天角色，也不是 Codex 子 Agent。

`Codex Agent Team` 不能替代 `src/Modules`，不能绕过 `Worker`、`Validator`、`Reviewer` 和 `QualityGate`。

## 当前开发入口

当前阶段的目标、合同、执行命令与验证记录见 [V2.3 阶段页](v2_3_engineering_planning_loop.md)。显式 `engineering_planning=true` 进入工程计划专用流程；模型仅生成待审查计划，平台严格校验原文、参数和证据范围，再用既有夹套模板生成 `CADModelSpec` / `FeatureGraph` / `SolidWorksBuildPlan`。原 Verified Worker、API evidence、几何验收、Reviewer、QualityGate 和发布门禁继续裁决真实结果。

上一阶段 [V2.2-D 轴类与夹套真实能力基线](v2_2_d_shaft_jacket_real_baseline.md) 已登记783/783测试通过、全局 self-check `Passed` 及三种受限轴类/夹套实证。V2.3 只选择其中夹套档案，不把既有通过值当作本轮完整验证。实时模型配置缺失必须拒绝；响应夹具重放、dry-run、真实 CAD 和实时模型稳定性分开报告。

## V2.2-C 历史开发记录

上一阶段为 [V2.2-C CAD 基线恢复与阵列证据重采](v2_2_c_cad_baseline_recovery.md)。2026-09-14 已完成既有 8 项 CAD 证据失败恢复、阵列引用与带符号方向修复、STEP 导出身份修复及重采；完整测试 684/684 通过，参数更新正式主流程为 `Passed` / `Deliverable`，发布件两格式独立复核通过。

9 月 9 日候选的旧 STEP 语义失配和 9 月 14 日中间连续导出失败均保留原始记录，生产绑定只使用最终修复后新采集并独立验收的证据。9 月 14 日全局 self-check 仍为 `Failed`：夹套生产证据与真实执行未激活，轴类真实工作流自检亦未通过。2026-09-24 保留文件与源码指纹复核通过，重新构建 0 警告、0 错误，完整测试仍为 684/684 通过；本次 self-check 退出码 2，恢复相关门禁与中文检查通过，全局仍保留上述既有缺口。见 [本次验证汇总](../output/validation/v2_2_c_cad_baseline/verification_summary_20260924.json)，不以历史结果代替本次运行。

默认工程流程仅做规划输入校验，具体 CAD 计划沿用原 Skill/Validator；该校验步骤不调用模型或生成计划。旧四角色确定性占位路线保留为显式兼容入口，不能把默认流程收敛描述为减少四次真实 LLM 推理。

前轮 [V2.2-B 强模型架构成果](v2_2_b_frontier_model_architecture.md) 和 [V2.2-A 任务生命周期与审批合同](v2_2_a_task_approval_lifecycle.md) 保留。V2.2-C/D 阶段允许受控真机诊断与主流程恢复；历史审查与旧 evidence 保留，未获有效证据的能力继续失败关闭。对应历史范围和实际验证记录以各阶段页为准。

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
