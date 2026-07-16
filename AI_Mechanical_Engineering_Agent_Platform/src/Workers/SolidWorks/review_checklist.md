# SolidWorks Worker 审查清单

## 必查项

- Worker 是否受请求级安全开关和环境变量保护。
- Worker 是否只通过平台调用。
- 真实执行是否默认关闭。
- 是否有 `build_report.json`。
- 是否有 `diagnostic_report.json`。
- 是否有可行动 `failure_stage`。
- 是否有 repair loop。
- 是否有 API evidence。
- 是否有 artifact validation。
- 是否有 `drawing_report.json`，并且工程图失败时 `failure_stage` 可行动。
- 是否 V1.1 工程图 smoke test 默认关闭，只在 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_SMOKE_TEST=true` 时执行。
- 是否工程图基础视图通过 `SolidWorksDrawingBuilder` 封装，而不是堆在 `RealSolidWorksWorker`。
- 是否工程图只包含 Front、Top、Right、Isometric 基础视图，没有越界实现尺寸、标题栏、BOM 或装配体工程图。
- 是否有 `dimension_report.json`，并且尺寸标注失败时 `failure_stage` 可行动。
- 是否 V1.2 工程图尺寸 smoke test 默认关闭，只在 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_DIMENSION_SMOKE_TEST=true` 时执行。
- 是否严格模式只由 `SW_STRICT_REAL_DRAWING_DIMENSION_TEST=true` 启用。
- 是否工程图尺寸通过 `SolidWorksDrawingDimensionBuilder` 封装，而不是堆在 `RealSolidWorksWorker`。
- 是否 V1.2 只添加 160 mm 长度、80 mm 宽度、12 mm 厚度、Φ10 孔径和孔中心距，没有越界实现 BOM、标题栏、国标模板美化、自动全尺寸标注、复杂公差、表面粗糙度、装配图、钣金展开图或 V1.3。
- 是否有 `title_block_report.json`，并且标题栏失败时 `failure_stage` 可行动。
- 是否 V1.3 工程图标题栏 smoke test 默认关闭，只在 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_TITLE_BLOCK_SMOKE_TEST=true` 时执行。
- 是否严格模式只由 `SW_STRICT_REAL_DRAWING_TITLE_BLOCK_TEST=true` 启用。
- 是否工程图标题栏通过 `SolidWorksDrawingTitleBlockBuilder` 封装，而不是堆在 `RealSolidWorksWorker`。
- 是否 V1.3 只写入 `plate_basic_4holes`、`PLATE-BASIC-4HOLES`、材料、比例、日期、版本 `A` 等最小标题栏信息，没有越界实现 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4。
- 是否 V1.4 发布包通过 `SolidWorksReleasePackageBuilder` 和 `SolidWorksReleasePackageValidator` 完成，只收集已有真实输出，不启动 SolidWorks。
- 是否生成 `release_manifest.json`、`package_quality_report.json` 和 `release_summary.md`。
- 是否 `solidworks_release_package_*` self-check 字段齐全，并且源文件缺失时返回可行动 `failure_stage`。
- 是否只检查文件存在、大小、路径、PDF 存在、`final_status` 和 `failure_stage`，没有越界做几何 OCR、PDF 视觉识别、BOM、装配图、批量出图、复杂图纸审查或 V1.5。
- 是否 V1.5 已把 `plate_basic_4holes` 真实 CAD 能力接入 `ChiefEngineerOrchestrator` → `SolidWorksWorkflowRouter` → `SequentialWorkflowEngine` → `SolidWorksMainWorkflowRunner` 主流程，而不是只停留在 self-check / smoke test。
- 是否泛化提到 `SolidWorks` 不会单独触发 CAD 主流程，只有显式 `solidworks_main_workflow`、结构化 `cad_model_type=plate_basic_4holes` 或具体 `plate_basic_4holes` 请求才触发。
- 是否 V1.5 主流程默认仍使用 `FakeSolidWorksWorker`，并且只有请求级 `allow_real_cad_execution=true`、`dry_run=false` 与环境变量 `SW_ENABLE_REAL_EXECUTION=true` 同时满足时才允许选择 `RealSolidWorksWorker`。
- 是否 V1.5 主流程经过 `SolidWorksBuildPlanValidator`、`SolidWorksArtifactValidator`、`SolidWorksBuildPlanReviewer` 和 `QualityGate`，并返回 `real_cad_executed`、`quality_gate_passed` 与 artifact 路径。
- 是否发布包新增 `package_build_status`、`all_source_reports_passed`、`source_reports_checked`、`source_report_failures`、`source_report_warnings` 和 `deliverable_status`，且源报告失败时 `deliverable_status=NotDeliverable`。
- 是否没有 Agent、Gateway、LLM 直接调用 Worker。
- 是否没有 COM 类型泄漏到 Contracts。
- 是否没有复制第三方 scripts。
- 是否没有破坏 `FakeSolidWorksWorker` dry-run。
- 是否 V1.7 CLI 通过 `AgentMessageDispatcher` → `chief-engineer`，而非直接调用 Worker、Builder 或 SmokeRunner。
- 是否 Router 仅接受结构化 `build_complete_drawing_package` 与 `part_type=plate_basic_4holes` 作为完整真实工作流触发条件。
- 是否四重确认缺失时 E2E report 为 `real_execution_confirmation_missing`，且没有 Fake 成功、Deliverable 或真实执行标记。
- 是否 Build、Drawing、Dimension、TitleBlock 使用同一 request 的绝对路径传递，阶段源输出仍在受信任 `output/solidworks/real/` 根内。
- 是否 E2E 发布包只使用显式 source set，不扫描 latest，也不把 SmokeRunner 诊断报告作为成功来源。
- 是否每个源报告 Passed、每个 real execution evidence 匹配 mode 且为真实执行，以及总体 QualityGate 通过后，才得到 `all_source_reports_passed=true`、`deliverable_status=Deliverable`。
- 是否 `e2e_execution_report.json` 包含请求、Gateway、Chief、WorkflowEngine、Router、Worker、连接、QualityGate、源报告、失败阶段与最终状态。

## V1.3 标题栏语义边界

- `title_block_report.json` 必须保留 `title_block_population_strategy=custom_properties_only`。
- `title_block_fields_verified_in_sheet_format` 必须保持 `false`，直到实现 Sheet Format note / `$PRP` 可见渲染校验。
- 不得把 `custom_properties_written=true` 或 `title_block_updated=true` 解释为国标标题栏格子已经可见填充。

## Blockers

默认启动 SolidWorks、真实文件缺失却返回 Passed、API 失败无 evidence、复制第三方脚本、Markdown 中文检查失败，均为 Blocker。

## 进入下一阶段条件

默认 self-check Passed；真实 smoke test 若执行失败，必须有明确 `failure_stage`、report 路径和下一步证据需求。

V1.1 进入 Claude 审查前，还必须确认 `solidworks_real_drawing_basic_views_implemented`、`solidworks_real_drawing_not_called_in_default_self_check`、`solidworks_drawing_failure_stage_actionable`、`solidworks_drawing_api_evidence_documented` 和 `solidworks_drawing_failure_repair_documented` 已写入 self-check 报告。

V1.2 进入 Claude 审查前，还必须确认 `solidworks_real_drawing_dimensions_implemented`、`solidworks_real_drawing_dimensions_default_disabled`、`solidworks_real_drawing_dimensions_requires_env_flag`、`solidworks_real_drawing_dimensions_not_called_in_default_self_check`、`solidworks_drawing_dimension_report_generated`、`solidworks_drawing_dimension_failure_stage_actionable`、`v1_2_version_stage_documented`、`solidworks_drawing_dimension_failure_repair_documented`、`solidworks_drawing_dimension_api_evidence_documented` 和 `solidworks_drawing_dimension_review_checklist_updated` 已写入 self-check 报告。

V1.3 进入 Claude 审查前，还必须确认 `solidworks_real_drawing_title_block_implemented`、`solidworks_real_drawing_title_block_default_disabled`、`solidworks_real_drawing_title_block_requires_env_flag`、`solidworks_real_drawing_title_block_not_called_in_default_self_check`、`solidworks_drawing_title_block_report_generated`、`solidworks_drawing_title_block_failure_stage_actionable`、`v1_3_version_stage_documented`、`solidworks_drawing_title_block_failure_repair_documented`、`solidworks_drawing_title_block_api_evidence_documented` 和 `solidworks_drawing_title_block_review_checklist_updated` 已写入 self-check 报告。

V1.4 进入 Claude 审查前，还必须确认 `solidworks_release_package_implemented`、`solidworks_release_package_default_no_cad_execution`、`solidworks_release_manifest_generated`、`solidworks_package_quality_report_generated`、`solidworks_release_summary_generated`、`solidworks_release_package_failure_stage_actionable`、`v1_4_version_stage_documented`、`solidworks_release_package_failure_repair_documented` 和 `solidworks_release_package_review_checklist_updated` 已写入 self-check 报告。若当前工作区缺少 V1.1/V1.2/V1.3 真实输出，可以进入实现审查，但不能把发布包标记为完整交付。

V1.5 进入 Claude 审查前，还必须确认 `real_cad_worker_integrated_into_main_workflow`、`chief_engineer_orchestrator_invokes_cad_workflow`、`workflow_engine_can_route_to_solidworks_worker`、`real_cad_main_workflow_default_disabled`、`real_cad_main_workflow_requires_request_flag`、`real_cad_main_workflow_requires_env_flag`、`real_cad_main_workflow_passes_quality_gate`、`release_package_all_source_reports_passed_field_exists`、`release_package_deliverable_status_field_exists`、`release_package_failed_source_reports_block_deliverable` 和 `v1_5_version_stage_documented` 已写入 self-check 报告。

V1.7 进入 Claude stage-gate 审查前，必须通过 build、test、self-check，并在用户显式开启真实执行后获得同次 E2E report。若真实运行失败，必须保留 `failure_stage`、对应阶段报告与总体 QualityGate 的失败结论；不得声称可交付。

## V1.1 Claude Improvements Backlog

V1.1 Claude 审查未发现 Blockers。以下 Improvements 已进入技术债，不阻塞 V1.2 主线：

- `ExecuteWithApplicationAsync` 后续增加更明确的执行超时边界。
- 清理历史切孔候选中的死代码和重复 report 写入。
- 统一 `repair_log` 命名和旧 evidence JSON 归档策略。
- 继续抽取工程图与零件保存、导出、COM 释放的共享工具。

## V1.2 Claude Improvements Backlog

V1.2 Claude 审查未发现 Blockers。以下 Improvements 已进入 `docs/technical_debt.md`，不阻塞 V1.3 主线：

- `ExecuteWithApplicationAsync` 真实执行阶段仍需补充强制超时边界。
- V1.2 尺寸标注为非关联、硬编码的最小策略，后续扩展必须继续暴露语义边界。
- V1.2 尺寸 Builder 内部失败分支后续应补更细的纯单元测试。
- 后续抽取工程图保存、PDF 导出、报告写入和 COM 释放共享工具。

## V1.7-REAL-AUTH 本地授权审查

- `config/solidworks.local.json` 必须被忽略，仓库只保留 `config/solidworks.local.example.json`。
- 只有 `LocalDevelopmentProfile` 可让 CLI 自动形成真实请求；CI、默认 self-check 和缺失配置的 CLI 不得启动 SolidWorks。
- `e2e_execution_report.json` 必须包含授权、启动尝试、连接、真实 Worker 调用和真实 CAD 执行字段。
- 授权通过后若真实执行失败，`failure_stage` 必须来自实际预检、连接或阶段报告，不能重新解释为确认缺失。
- Gateway、Agent、LLM 不得直接调用 Worker；不得用 SmokeRunner、Builder 或文件存在冒充主流程成功。
