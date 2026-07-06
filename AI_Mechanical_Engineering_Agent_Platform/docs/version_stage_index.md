# 版本阶段索引

## 阶段列表

| 阶段 | 目标 | 不做什么 | 关键交付物 | 说明文件 | self-check 字段 | 审查报告位置 |
|---|---|---|---|---|---|---|
| V0.1 | 平台骨架 | 不做真实 CAD | Contracts、Registry、Gateway、CLI | `docs/architecture.md` | `solution_exists` | `../reviewrep` |
| V0.2 | Internal Agent Routing | 不暴露 Internal Agent | `InternalAgentRouter` | `docs/agent_standard.md` | `internal_routing_enabled` | `../reviewrep` |
| V0.3 | Microsoft Runtime Adapter | 不污染业务层 | `AgentRuntime.Microsoft` | `src/AgentRuntime.Microsoft/README.md` | `microsoft_runtime_dependency_isolated` | `../reviewrep` |
| V0.4 | Workflow Quality Loop | 不做业务 CAD | Retry、FailureReport、HumanApproval | `docs/quality_gate.md` | `workflow_quality_loop_enabled` | `../reviewrep` |
| V0.5/V0.6 | Workflow-backed Internal Orchestration | 不绕过 WorkflowEngine | Internal Agent Workflow | `docs/architecture.md` | `chief_engineer_internal_orchestration_uses_workflow_engine` | `../reviewrep` |
| V0.7 | chief-engineer 真实 LLM Runtime | 不让 LLM 调 Worker | Runtime Invoker、Output Mapper | `src/AgentRuntime.Microsoft/README.md` | `real_runtime_invoker_implemented` | `../reviewrep` |
| V0.8 | Runtime Reliability Hardening | 不扩展真实 CAD | Retry delay、HTTP timeout | `docs/quality_gate.md` | `retry_delay_actually_awaited` | `../reviewrep` |
| V0.9-A | Markdown 中文规范与外部 Skill 参考策略 | 不复制外部源码 | 中文检查、参考分析 | `docs/markdown_standard.md` | `markdown_chinese_check_passed` | `../reviewrep` |
| V0.9-B | SolidWorks Module Skeleton | 不接真实 CAD | BuildPlan、FakeWorker、Validator | `src/Modules/CADModeling/execution.md` | `solidworks_module_skeleton_enabled` | `../reviewrep` |
| V1.0-A | RealSolidWorksWorker Preflight + Session Boundary | 不真实建模 | Preflight、SessionManager、安全开关 | `src/Workers/SolidWorks/execution.md` | `solidworks_real_worker_skeleton_exists` | `../reviewrep` |
| V1.0-B | plate_basic_4holes 最小真实建模 | 不做工程图和装配体 | SLDPRT、STEP、build_report | `src/Workers/SolidWorks/execution.md` | `solidworks_real_plate_build_implemented` | `../reviewrep` |
| V1.0-B-DIAG | SolidWorks 诊断隔离 | 不改 Gateway | SmokeRunner、diagnostic_report | `src/Workers/SolidWorks/failure_repair.md` | `solidworks_diagnostic_runner_exists` | `../reviewrep` |
| V1.0-B-REPAIR | API Evidence Driven Repair Loop | 不盲改 API | Analyzer、EvidenceCollector、PlateFeatureBuilder | `src/Workers/SolidWorks/api_evidence.md` | `solidworks_api_repair_loop_available` | `../reviewrep` |
| V1.0-DOCS-AGENTS | 可执行文档层与 Codex Agent Team | 不继续修 API | 文档协议、Codex Agents、Skills 骨架 | `docs/codex_agent_team_guide.md` | `executable_docs_layer_enabled` | `../reviewrep` |
| V1.1-GOVERNED-DRAWING | Codex Agent 复用治理与 SolidWorks 真实工程图基础视图 | 不新增同职责 Agent，不做尺寸、标题栏、BOM、装配体或复杂模板 | `codex_agent_registry.md`、`codex_agent_governance.md`、`SolidWorksDrawingBuilder`、`SolidWorksDrawingSmokeRunner`、`drawing_report.json`、SLDDRW、PDF | `docs/codex_agent_governance.md`、`src/Workers/SolidWorks/execution.md`、`src/Workers/SolidWorks/api_evidence.md`、`src/Workers/SolidWorks/failure_repair.md` | `codex_only_canonical_agents_active`、`solidworks_real_drawing_basic_views_implemented`、`solidworks_real_drawing_not_called_in_default_self_check`、`solidworks_drawing_failure_stage_actionable` | `../reviewrep` |
| V1.1 | SolidWorks 真实工程图基础视图 | 不做尺寸、标题栏、BOM、装配体或复杂模板 | `SolidWorksDrawingBuilder`、`SolidWorksDrawingSmokeRunner`、`drawing_report.json`、SLDDRW、PDF | `src/Workers/SolidWorks/execution.md`、`src/Workers/SolidWorks/api_evidence.md`、`src/Workers/SolidWorks/failure_repair.md` | `solidworks_real_drawing_basic_views_implemented`、`solidworks_real_drawing_not_called_in_default_self_check`、`solidworks_drawing_failure_stage_actionable` | `../reviewrep` |
| V1.2 | SolidWorks 工程图基础尺寸标注 | 不做 BOM、标题栏、国标模板美化、自动全尺寸标注、复杂公差、表面粗糙度、装配图、钣金展开图或 V1.3 | `SolidWorksDrawingDimensionBuilder`、`SolidWorksDrawingDimensionSmokeRunner`、`dimension_report.json`、带尺寸 SLDDRW、带尺寸 PDF | `src/Workers/SolidWorks/execution.md`、`src/Workers/SolidWorks/api_evidence.md`、`src/Workers/SolidWorks/failure_repair.md`、`src/Workers/SolidWorks/review_checklist.md` | `solidworks_real_drawing_dimensions_implemented`、`solidworks_real_drawing_dimensions_not_called_in_default_self_check`、`solidworks_drawing_dimension_failure_stage_actionable`、`v1_2_version_stage_documented` | `../reviewrep` |
| V1.3 | SolidWorks 工程图模板与标题栏基础信息 | 不做 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4 | `SolidWorksDrawingTitleBlockBuilder`、`SolidWorksDrawingTitleBlockSmokeRunner`、`title_block_report.json`、带标题栏信息 SLDDRW、PDF | `src/Workers/SolidWorks/execution.md`、`src/Workers/SolidWorks/api_evidence.md`、`src/Workers/SolidWorks/failure_repair.md`、`src/Workers/SolidWorks/review_checklist.md` | `solidworks_real_drawing_title_block_implemented`、`solidworks_real_drawing_title_block_not_called_in_default_self_check`、`solidworks_drawing_title_block_failure_stage_actionable`、`v1_3_version_stage_documented` | `../reviewrep` |

## 使用方式

开始新任务时先定位阶段，再读取对应说明文件。阶段未完成时不得提前进入下一阶段。
