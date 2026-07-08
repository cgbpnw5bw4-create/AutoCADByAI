# CADModeling 审查清单

## Blockers

- 默认 self-check 启动真实 CAD。
- `Agent`、`Gateway` 或 `LLM` 直接调用 Worker。
- `Skill` 直接执行 CAD 操作。
- COM 类型泄漏到 Contracts、PlatformCore 或 QualityGate。
- 失败只记录 `Failed`，没有可行动 `failure_stage`。
- API 失败没有 evidence report。
- 复制第三方 `scripts` 源码。
- V1.2 尺寸标注默认 self-check 启动 SolidWorks。
- V1.2 尺寸标注没有 `dimension_report.json` 或没有可行动 `failure_stage`。
- V1.3 标题栏测试默认 self-check 启动 SolidWorks。
- V1.3 标题栏没有 `title_block_report.json` 或没有可行动 `failure_stage`。
- V1.3 借标题栏扩展到 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4。
- V1.4 发布包缺少 `release_manifest.json`、`package_quality_report.json` 或 `release_summary.md`。
- V1.4 源 artifact 或 report 缺失时没有可行动 `failure_stage`。
- V1.4 借发布包扩展到 BOM、装配图、批量出图、复杂图纸审查、几何 OCR、PDF 视觉识别或 V1.5。
- V1.5 主流程仍没有从 `ChiefEngineerOrchestrator` 经 `SolidWorksWorkflowRouter`、`SequentialWorkflowEngine` 接到 `SolidWorksMainWorkflowRunner`。
- V1.5 泛化提到 `SolidWorks` 就直接触发 CAD 主流程，而不是要求显式 flag、结构化 `cad_model_type=plate_basic_4holes` 或具体 `plate_basic_4holes` 请求。
- V1.5 主流程绕过 Validator、Reviewer 或 QualityGate。
- V1.5 真实 CAD 只满足请求级或环境级单一开关时就启动 SolidWorks。
- V1.5 发布包源报告失败时仍把 `deliverable_status` 标记为可交付。
- Markdown 中文检查失败。

## V1.3 标题栏语义边界

- `title_block_report.json` 必须保留 `title_block_population_strategy=custom_properties_only`。
- `title_block_fields_verified_in_sheet_format` 必须保持 `false`，直到实现 Sheet Format note / `$PRP` 可见渲染校验。
- 不得把 `custom_properties_written=true` 或 `title_block_updated=true` 解释为国标标题栏格子已经可见填充。

## Improvements

- evidence 报告可以更细化候选策略。
- diagnostic report 可以增加更多中间对象状态。
- Reviewer 规则可以增加工程合理性检查。
- V1.2 后续可研究关联孔中心尺寸，但必须先补 `IView.GetVisibleEntities2` 和实体选择证据。
- V1.3 后续可研究模板字段映射和国标标题栏样式，但必须先补官方 API 证据和模板兼容测试。
- V1.4 后续可扩展签名、压缩包、校验和发布审计，但必须先保持最小发布包质量检查稳定。

## 进入下一阶段条件

必须通过 build、test、self-check。涉及真实 CAD 的能力必须有默认关闭开关、诊断 Runner、artifact validation 和 QualityGate 记录。

V1.2 还必须确认 `solidworks_real_drawing_dimensions_implemented`、`solidworks_real_drawing_dimensions_not_called_in_default_self_check`、`solidworks_drawing_dimension_failure_stage_actionable`、`solidworks_drawing_dimension_api_evidence_documented` 和 `solidworks_drawing_dimension_failure_repair_documented` 为 true。

V1.3 还必须确认 `solidworks_real_drawing_title_block_implemented`、`solidworks_real_drawing_title_block_not_called_in_default_self_check`、`solidworks_drawing_title_block_failure_stage_actionable`、`solidworks_drawing_title_block_api_evidence_documented`、`solidworks_drawing_title_block_failure_repair_documented` 和 `v1_3_version_stage_documented` 为 true。

V1.4 还必须确认 `solidworks_release_package_implemented`、`solidworks_release_package_default_no_cad_execution`、`solidworks_release_manifest_generated`、`solidworks_package_quality_report_generated`、`solidworks_release_summary_generated`、`solidworks_release_package_failure_stage_actionable`、`solidworks_release_package_failure_repair_documented` 和 `v1_4_version_stage_documented` 为 true。

V1.5 还必须确认 `real_cad_worker_integrated_into_main_workflow`、`chief_engineer_orchestrator_invokes_cad_workflow`、`workflow_engine_can_route_to_solidworks_worker`、`real_cad_main_workflow_default_disabled`、`real_cad_main_workflow_requires_request_flag`、`real_cad_main_workflow_requires_env_flag`、`real_cad_main_workflow_passes_quality_gate`、`release_package_all_source_reports_passed_field_exists`、`release_package_deliverable_status_field_exists`、`release_package_failed_source_reports_block_deliverable` 和 `v1_5_version_stage_documented` 为 true。
