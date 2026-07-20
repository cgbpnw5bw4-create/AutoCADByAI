---
name: quality-review
description: "用于项目质量审查，检查架构边界、self-check 字段、分层规则、真实 CAD 默认关闭、第三方脚本边界和 Markdown 中文规范。"
---

# 质量审查技能

## 审查范围

检查 Agent、Skill、Worker、Validator、Reviewer、QualityGate 分层是否被破坏。检查 Gateway 是否仍只暴露 `chief-engineer`，Internal Agent 是否仍不能被外部直接调用。

## 必查项

- self-check 字段是否覆盖新增能力。
- 是否默认执行真实 CAD。
- 是否复制第三方脚本。
- 是否让 Agent、Gateway 或 LLM 直接调用 Worker。
- active `.codex/agents/` 是否只包含 `docs/codex_agent_registry.md` 登记的 canonical agents。
- 是否存在同职责重复 `api_researcher`、`code_mapper`、`quality_gate`、`docs_writer` 等 Codex Agent。
- V1.1 工程图 smoke test 是否默认关闭，并且只在 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_SMOKE_TEST=true` 时执行。
- V1.2 工程图尺寸 smoke test 是否默认关闭，并且只在 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_DIMENSION_SMOKE_TEST=true` 时执行。
- V1.2 是否只做基础尺寸标注，并且 `dimension_report.json`、failure_stage、self-check 字段和 Markdown 中文检查齐全。
- V1.3 工程图标题栏 smoke test 是否默认关闭，并且只在 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_TITLE_BLOCK_SMOKE_TEST=true` 时执行。
- V1.3 是否只做标题栏基础信息，并且 `title_block_report.json`、failure_stage、self-check 字段和 Markdown 中文检查齐全。
- V1.3 是否没有越界实现 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4。
- V1.4 是否只做工程发布包与最小质量检查，并且 `release_manifest.json`、`package_quality_report.json`、`release_summary.md`、failure_stage、self-check 字段和 Markdown 中文检查齐全。
- V1.4 是否默认不启动 SolidWorks、不做几何 OCR、不做 PDF 视觉识别、不做 BOM、装配图、批量出图、复杂图纸审查或 V1.5。
- V1.5 是否只做真实 CAD 主工作流集成，不新增 BOM、新 CAD 子功能、更多工程图能力、装配体或 V1.6。
- V1.5 是否通过 `ChiefEngineerOrchestrator`、`SequentialWorkflowEngine`、`SolidWorksMainWorkflowRunner`、Worker、Validator、Reviewer 和 QualityGate 串接主流程。
- V1.5 默认是否仍走 `FakeSolidWorksWorker`，并且真实执行是否同时要求请求级 `allow_real_cad_execution=true`、`dry_run=false` 和环境变量 `SW_ENABLE_REAL_EXECUTION=true`。
- V1.5 发布包是否区分 `package_build_status`、`all_source_reports_passed` 和 `deliverable_status`，并且源报告失败时 `deliverable_status=NotDeliverable`。
- V1.5 self-check 是否包含 `real_cad_worker_integrated_into_main_workflow`、`chief_engineer_orchestrator_invokes_cad_workflow`、`workflow_engine_can_route_to_solidworks_worker`、`real_cad_main_workflow_default_disabled`、`release_package_all_source_reports_passed_field_exists`、`release_package_deliverable_status_field_exists`、`release_package_failed_source_reports_block_deliverable` 和 `v1_5_version_stage_documented`。
- Markdown 中文检查是否通过。

## 输出格式

输出 Blockers、Improvements、测试结果和是否可以进入下一阶段。

## V1.8 参数化零件族审查

### 目标与适用范围

审查通用 `CADModelSpec`、`PartTypeRegistry`、三个零件族的独立定义和构建器，以及执行器之前的参数拒绝。输入为源码、测试、自检报告和接口证据；输出为阻断项、改进项、模拟执行或回归结果和审查建议。

### 执行步骤与验证标准

1. 核对通用建模规格的七个字段，并确认零件类型注册表包含三个族。
2. 确认每族都有参数模式、校验器、构建计划、构建器、`failure_stage`、接口证据和测试。
3. 确认 `unsupported_part_type`、`missing_required_parameter`、`invalid_parameter_value` 在 Worker 之前停止。
4. 检查不存在大型 `switch(part_type)`，并运行 plate 回归、flange dry-run、shaft dry-run。
5. 运行 build、test 和默认 self-check，确认不启动 SolidWorks。

通过标准是 `generic_cad_model_spec_supported`、`part_type_registry_exists`、`plate_part_family_registered`、`flange_part_family_registered`、`shaft_part_family_registered`、`unsupported_part_type_rejected`、`invalid_part_parameters_rejected_before_worker`、`part_family_builders_do_not_use_large_switch`、`plate_regression_passed`、`flange_dry_run_passed`、`shaft_dry_run_passed`、`real_cad_part_family_default_disabled`、`v1_8_version_stage_documented`、`markdown_chinese_check_passed` 全部为 `true`。

### 常见失败与禁止事项

未注册类型回退 plate、非法参数进入 Worker、大型类型 switch、plate 能力退化、flange / shaft dry-run 失败、默认启动 SolidWorks 或以候选 API 冒充真实验收，均为 Blocker。禁止越界实现装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9。`flange_basic` 和 `shaft_basic` 本轮只可声称 dry-run 通过；真实验收分别等待独立 flange smoke 与 shaft 旋转专用证据。
