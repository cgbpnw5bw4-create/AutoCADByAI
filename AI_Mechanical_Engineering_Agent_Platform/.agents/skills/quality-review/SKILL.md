---
name: quality-review
description: "用于项目质量审查，检查架构边界、self-check 字段、SolidWorks V2.0 执行策略、第三方脚本边界和 Markdown 中文规范。"
---

# 质量审查技能

## 审查范围

检查 Agent、Skill、Worker、Validator、Reviewer、QualityGate 分层是否被破坏。检查 Gateway 是否仍只暴露 `chief-engineer`，Internal Agent 是否仍不能被外部直接调用。

## 必查项

- self-check 字段是否覆盖新增能力。
- V2.0 本地交互式 `dry_run=false` 是否默认执行真实 CAD，同时 CI、单元测试、self-check、dry-run 和显式禁用是否保持不启动。
- 真实 Worker 是否在 COM 连接前探测交互桌面与 SolidWorks COM 注册，失败时是否 fail-closed 且不回退 Fake Worker。
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
- V1.5 历史回归证据是否保留当时的 Fake 默认与双确认语义；该历史规则不得覆盖 V2.0 当前默认启用策略。
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

未注册类型回退 plate、非法参数进入 Worker、大型类型 switch、plate 能力退化、flange / shaft dry-run 失败、默认 self-check/CI/单元测试启动 SolidWorks，或以候选 API 冒充真实验收，均为 Blocker。禁止越界实现装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9。`flange_basic` 和 `shaft_basic` 本轮只可声称 dry-run 通过；真实验收分别等待独立 flange smoke 与 shaft 旋转专用证据。

## V2.0-D 模型重建与几何验证审查

审查 V2.0-D 时，先确认参数更新闭环严格为：`CADModelSpec` → `ModelUpdateService` → `BuildPlanCompiler` → `FeatureExecutionPipeline` → `SolidWorks Rebuild` → 独立 `GeometryReader` → `GeometryValidator` → `Artifact Validator / Reviewer / QualityGate`。`ModelUpdateService`、`GeometryValidator` 和质量链不得直接读取 `COM`；真实读取只允许发生在独立 `GeometryReader` 的当前受控模型边界。

必须检查：

- 更新审计同时记录 `old_parameters`、`new_parameters`、`changed_features`、`rebuild_result`，且 `FeatureGraph` 未被破坏。
- `GeometryValidator` 使用真实 `SolidWorks` 输出验证 `BoundingBox`、实体数量、体积、可用质量属性、草图、拉伸、切除、孔，以及 `length_mm`、`diameter_mm` / 轴径。
- `geometry_validation_report.json` 包含 `model_id`、`input_parameters`、`measured_geometry`、`expected_geometry`、`deviations`、`passed_checks`、`failed_checks`、`failure_stage`、`final_status`；`rebuild_report.json` 与其同次产生，并都被 `QualityGate` 当作源报告。
- `rebuild_failed`、`geometry_read_failed`、`bounding_box_invalid`、`volume_validation_failed`、`parameter_geometry_mismatch`、`feature_missing_after_rebuild`、`geometry_report_failed` 任一项均失败关闭；`COM` 返回、文件存在、历史产物和诊断结果均不能通过审查。
- `plate_basic_4holes` 的四个孔必须真实读取并逐个证明；不允许为此新增 CAD Feature 或零件族，也不允许绕过 `FeatureHandler`、`QualityGate` 或使用未取证的阵列、贯穿、对称拉伸、孔向导等策略。
- 真实验收只能执行 `run-cad-workflow --input examples/parameter_update_plate.json`，覆盖先 160×80×12、后 200×100×15 的变化；禁止直接调用构建器，也不得进入 V2.0-E。

以下 self-check 字段必须全部为 true：

~~~text
model_rebuild_pipeline_exists
parameter_update_supported
solidworks_rebuild_supported
geometry_validator_exists
bounding_box_validation_supported
volume_validation_supported
parameter_geometry_match_supported
rebuild_failure_detected
geometry_report_generated
v2_0_d_documented
markdown_chinese_check_passed
~~~

缺少真实 GeometryReader 数据、任一报告无效、FeatureGraph 损坏、四孔未证明或 QualityGate 未通过，均为 Blocker。

## V2.1-B 孔增强审查补充

### 目标、范围与输入输出

审查四类孔的定义、参数、引用、标准化、证据和后置几何链。输入为当前代码、测试、自检、能力矩阵与诊断；输出为可行动阻断、改进和验证边界。以 `docs/v2_1_b_hole_features.md` 与两层 `review_checklist.md` 为详细清单。

### 执行与验证

确认复用 canonical agents 与唯一 `HoleHandler`；所有非法类型/尺寸/孔位/面/数量/螺纹请求和编译计划篡改在 Worker 前拒绝。四个 dry-run 及十二个自检字段应有行为证据。新四类显式孔 API 未取证时必须真实失败关闭；旧简单孔的重新采证不能自动授权新 profile。孔几何检查必须使用独立 DTO，覆盖缺值、错数量、错尺寸、沉孔/沉头失配及缺失攻丝元数据反例，并进入 QualityGate。

### 失败与禁止

Handler COM、未证实 API 实调、普通 Cut 冒充攻丝、非空 Feature 冒充几何、期望值充当测量、能力基线削弱均为阻断。V2.1-A 报告独立性受限且无 Blockers，其改进按用户要求仅登记 backlog，不阻塞本轮。必须分开报告阶段、自检总体和真实验收状态；本轮不进入 V2.1-C。
