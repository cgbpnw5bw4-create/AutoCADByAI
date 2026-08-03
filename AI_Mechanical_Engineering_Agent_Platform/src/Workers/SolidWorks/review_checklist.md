# SolidWorks Worker 审查清单

## 必查项

- Worker 是否受请求级安全开关和环境变量保护。
- Worker 是否只通过平台调用。
- 本地交互式真实执行是否默认启用，CI、单元测试、dry-run 和显式禁用是否关闭。
- 真实 Worker 是否在 COM 连接前只读探测 Windows、交互式桌面与 `SldWorks.Application` 注册，失败时是否使用 `real_execution_environment_unavailable` 且不回退 Fake Worker。
- 是否有 `build_report.json`。
- 是否有 `diagnostic_report.json`。
- 是否有可行动 `failure_stage`。
- 是否有 repair loop。
- 是否有 API evidence。
- 是否有 artifact validation。
- 是否有 `drawing_report.json`，并且工程图失败时 `failure_stage` 可行动。
- 工程图 diagnostic 是否与 CLI 主流程隔离，且 self-check 不执行真实工程图。
- 是否工程图基础视图通过 `SolidWorksDrawingBuilder` 封装，而不是堆在 `RealSolidWorksWorker`。
- 是否工程图只包含 Front、Top、Right、Isometric 基础视图，没有越界实现尺寸、标题栏、BOM 或装配体工程图。
- 是否有 `dimension_report.json`，并且尺寸标注失败时 `failure_stage` 可行动。
- 工程图尺寸 diagnostic 是否与 CLI 主流程隔离，且 self-check 不执行真实尺寸。
- 是否严格模式只由 `SW_STRICT_REAL_DRAWING_DIMENSION_TEST=true` 启用。
- 是否工程图尺寸通过 `SolidWorksDrawingDimensionBuilder` 封装，而不是堆在 `RealSolidWorksWorker`。
- 是否 V1.2 只添加 160 mm 长度、80 mm 宽度、12 mm 厚度、Φ10 孔径和孔中心距，没有越界实现 BOM、标题栏、国标模板美化、自动全尺寸标注、复杂公差、表面粗糙度、装配图、钣金展开图或 V1.3。
- 是否有 `title_block_report.json`，并且标题栏失败时 `failure_stage` 可行动。
- 工程图标题栏 diagnostic 是否与 CLI 主流程隔离，且 self-check 不执行真实标题栏。
- 是否严格模式只由 `SW_STRICT_REAL_DRAWING_TITLE_BLOCK_TEST=true` 启用。
- 是否工程图标题栏通过 `SolidWorksDrawingTitleBlockBuilder` 封装，而不是堆在 `RealSolidWorksWorker`。
- 是否 V1.3 只写入 `plate_basic_4holes`、`PLATE-BASIC-4HOLES`、材料、比例、日期、版本 `A` 等最小标题栏信息，没有越界实现 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4。
- 是否 V1.4 发布包通过 `SolidWorksReleasePackageBuilder` 和 `SolidWorksReleasePackageValidator` 完成，只收集已有真实输出，不启动 SolidWorks。
- 是否生成 `release_manifest.json`、`package_quality_report.json` 和 `release_summary.md`。
- 是否 `solidworks_release_package_*` self-check 字段齐全，并且源文件缺失时返回可行动 `failure_stage`。
- 是否只检查文件存在、大小、路径、PDF 存在、`final_status` 和 `failure_stage`，没有越界做几何 OCR、PDF 视觉识别、BOM、装配图、批量出图、复杂图纸审查或 V1.5。
- 是否 V1.5 已把 `plate_basic_4holes` 真实 CAD 能力接入 `ChiefEngineerOrchestrator` → `SolidWorksWorkflowRouter` → `SequentialWorkflowEngine` → `SolidWorksMainWorkflowRunner` 主流程，而不是只停留在 self-check / smoke test。
- 是否泛化提到 `SolidWorks` 不会单独触发 CAD 主流程，只有显式 `solidworks_main_workflow`、结构化 `cad_model_type=plate_basic_4holes` 或具体 `plate_basic_4holes` 请求才触发。
- 是否 V2.0 本地交互式主流程默认选择 `RealSolidWorksWorker`，并在 dry-run、显式禁用、CI、单元测试或强制 Fake Worker 时选择 `FakeSolidWorksWorker`。
- 是否 V1.5 主流程经过 `SolidWorksBuildPlanValidator`、`SolidWorksArtifactValidator`、`SolidWorksBuildPlanReviewer` 和 `QualityGate`，并返回 `real_cad_executed`、`quality_gate_passed` 与 artifact 路径。
- 是否发布包新增 `package_build_status`、`all_source_reports_passed`、`source_reports_checked`、`source_report_failures`、`source_report_warnings` 和 `deliverable_status`，且源报告失败时 `deliverable_status=NotDeliverable`。
- 是否没有 Agent、Gateway、LLM 直接调用 Worker。
- 是否没有 COM 类型泄漏到 Contracts。
- 是否没有复制第三方 scripts。
- 是否没有破坏 `FakeSolidWorksWorker` dry-run。
- 是否 V1.7 CLI 通过 `AgentMessageDispatcher` → `chief-engineer`，而非直接调用 Worker、Builder 或 SmokeRunner。
- 是否 Router 仅接受结构化 `build_complete_drawing_package` 与 `part_type=plate_basic_4holes` 作为完整真实工作流触发条件。
- 是否执行策略禁用时 E2E report 使用明确禁用阶段，且没有 Fake 冒充 Deliverable 或真实执行标记。
- 是否 Build、Drawing、Dimension、TitleBlock 使用同一 request 的绝对路径传递，阶段源输出仍在受信任 `output/solidworks/real/` 根内。
- 是否 E2E 发布包只使用显式 source set，不扫描 latest，也不把 SmokeRunner 诊断报告作为成功来源。
- 是否每个源报告 Passed、每个 real execution evidence 匹配 mode 且为真实执行，以及总体 QualityGate 通过后，才得到 `all_source_reports_passed=true`、`deliverable_status=Deliverable`。
- 是否 `e2e_execution_report.json` 包含请求、Gateway、Chief、WorkflowEngine、Router、Worker、连接、QualityGate、源报告、失败阶段与最终状态。

## V1.3 标题栏语义边界

- `title_block_report.json` 必须保留 `title_block_population_strategy=custom_properties_only`。
- `title_block_fields_verified_in_sheet_format` 必须保持 `false`，直到实现 Sheet Format note / `$PRP` 可见渲染校验。
- 不得把 `custom_properties_written=true` 或 `title_block_updated=true` 解释为国标标题栏格子已经可见填充。

## Blockers

默认 self-check、CI、单元测试或 dry-run 启动 SolidWorks，真实文件缺失却返回 Passed，环境探测失败仍连接 COM，API 失败无 evidence，复制第三方脚本或 Markdown 中文检查失败，均为 Blocker。本地交互式 `dry_run=false` 默认真实执行是 V2.0 既定策略，不属于 Blocker。

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

## V2.0 本地可选配置审查

- `config/solidworks.local.json` 必须被忽略，仓库只保留 `config/solidworks.local.example.json`。
- 本地配置只提供模板、可见性和超时；缺失配置不阻止本地交互默认执行，CI、self-check 和单元测试不得启动 SolidWorks。
- `e2e_execution_report.json` 必须包含授权、启动尝试、连接、真实 Worker 调用和真实 CAD 执行字段。
- 授权通过后若真实执行失败，`failure_stage` 必须来自实际预检、连接或阶段报告，不能重新解释为确认缺失。
- Gateway、Agent、LLM 不得直接调用 Worker；不得用 SmokeRunner、Builder 或文件存在冒充主流程成功。

## V1.8 零件族审查

### 目标、适用范围与输入输出

审查 `PartTypeRegistry`、三个零件族定义和构建器、执行分发、前置拒绝、模拟执行、接口证据和四孔板回归。输入为源码、测试、自检报告、执行报告和诊断证据；输出为阻断项、改进项、验证结果与真实验收建议。

### 必查项

- Registry 是否明确注册 `plate_basic_4holes`、`flange_basic`、`shaft_basic`，未注册键是否返回 `unsupported_part_type`。
- 参数非法时是否返回 `missing_required_parameter` 或 `invalid_parameter_value`，并且 Worker 和 `SolidWorksSessionManager` 调用计数为零。
- 每族是否有独立 Schema、Validator、BuildPlan 生成器、`IPartFamilyBuilder`、`failure_stage`、API evidence 和测试。
- Worker 是否只通过 Registry 取得 Builder，而非堆叠大型 `switch(part_type)` 或多处零件族字符串分支。
- `plate_basic_4holes` 是否仍使用已验证的 `SolidWorksPlateFeatureBuilder` 与原有真实主工作流程，且回归测试通过。
- `flange_basic` 的 BuildPlan 是否包含外径、内径、厚度、螺栓孔数、螺栓孔直径和分布圆直径，并通过 dry-run。
- `shaft_basic` 的 BuildPlan 是否包含直径、长度及成对的可选台阶直径/长度列表，并通过 dry-run。
- `flange_basic` 真实入口前是否有 `CreateCircle`、`FeatureExtrusion2`、`FeatureCut4` 的独立 smoke 证据；没有时是否明确保持 dry-run-only。
- `shaft_basic` 真实入口前是否有 `CreateLine`、`CreateCenterLine`、`FeatureRevolve2` 的专用诊断证据；没有时是否明确保持 dry-run-only。
- 默认 self-check 是否不连接 COM、不启动 SolidWorks，并设置 `real_cad_part_family_default_disabled=true`。
- 真实路径是否仍通过 `ChiefEngineerOrchestrator` → `WorkflowEngine` → Router → Worker → Validator → Reviewer → `QualityGate`，随后才进入 Drawing 与 ReleasePackage。

### Blockers

- 非法参数或未注册类型进入真实 Worker。
- 使用大型 `switch(part_type)` 替代 Registry 与多态 Builder。
- `plate_basic_4holes` 真实能力或回归测试退化。
- `flange_basic` 或 `shaft_basic` dry-run 失败，或将 dry-run 表述为真实验收。
- API evidence 不足时盲改主 Worker，或绕过 ArtifactValidator、Reviewer、QualityGate。
- 默认 self-check、CI、单元测试或 dry-run 启动 SolidWorks，或越界实现装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量队列、V1.9。

### 验证步骤与通过标准

1. 运行 build、test 和默认 self-check。
2. 核对三族注册、两类前置拒绝、无大型 switch、plate 回归及 flange / shaft dry-run 字段。
3. 确认 `generic_cad_model_spec_supported`、`part_type_registry_exists`、`plate_part_family_registered`、`flange_part_family_registered`、`shaft_part_family_registered`、`unsupported_part_type_rejected`、`invalid_part_parameters_rejected_before_worker`、`part_family_builders_do_not_use_large_switch`、`plate_regression_passed`、`flange_dry_run_passed`、`shaft_dry_run_passed`、`real_cad_part_family_default_disabled`、`v1_8_version_stage_documented`、`markdown_chinese_check_passed` 全部为 `true`。

上述条件满足时可进入 Claude 实现审查。`flange_basic` 在独立 smoke 通过后才可进入真实验收；`shaft_basic` 在旋转专用证据和独立 smoke 通过后才可进入真实验收。本轮不进入 V1.9。

## V1.9 Phase 1 Worker 审查

### 目标与输入输出

审查 flange / shaft 真实 Builder、`RealSolidWorksWorker` 分发、真实 build-only 报告和发布包，并确认 plate 完整图包回归。输入为源码、测试、self-check、API evidence、同次真实报告和产物；输出为 Blockers、Improvements、实现审查结论和真实 smoke 待回填项。

### 必查项

- `PartFamilyBuilderRegistry` 是否解析 flange / shaft 真实 Builder，且无大型 `switch(part_type)`。
- `RealSolidWorksWorker` 是否返回 `RealBuildFlangeBasic` / `RealBuildShaftBasic`，并统一记录保存、STEP 导出和 API evidence。
- 是否使用 V2.0 统一运行策略；self-check、CI、单元测试和 dry-run 是否不连接 COM。
- 真实 SolidWorks 是否全局串行，并按 flange → shaft 验收。
- flange 是否分开外圆拉伸、中心孔切除和螺栓孔切除，是否拒绝 `HoleWizard` / 圆周阵列。
- shaft 是否使用闭合线段轮廓、中心线 selection mark `16` 和 `FeatureRevolve2` 360°，是否拒绝本轮混用偏移多段拉伸。
- 两族是否仅进入 build-only，没有调用工程图 Builder；plate 完整包是否回归通过。
- 是否使用十二个专用 `failure_stage`，且 `part_family_api_evidence_insufficient` 会在证据不足时失败关闭。
- 发布包是否位于 `output/solidworks/e2e/<part_type>/<timestamp>/`，并包含 SLDPRT、STEP、构建报告、端到端报告和 manifest。
- 最终验收是否通过 Gateway / Agent / WorkflowEngine / Router / Registry / Worker / Validator / Reviewer / QualityGate，而非直接 Builder / SmokeRunner。
- Phase 1 文档是否明确入场时真实 smoke 尚待回填，Phase 2 是否用同次 CLI 主工作流程证据关闭该状态。

### Blockers

- flange / shaft 任一 Builder 缺失、无 API evidence 门禁或只返回泛化失败阶段。
- 默认 self-check 启动 SolidWorks、缺少任一授权仍执行、或真实 COM 并发。
- flange / shaft 自动工程图，或 plate 完整包回归退化。
- SLDPRT / STEP 缺失或为空却返回成功，或 QualityGate 拒绝后仍生成可交付包。
- 用 Builder / SmokeRunner、历史 latest 或文件存在冒充最终验收。
- 大型类型 switch、绕过 Registry、跳过 Validator / Reviewer / QualityGate 或进入 V2.0。

### 自检字段和通过标准

```text
flange_real_builder_implemented
shaft_real_builder_implemented
flange_real_workflow_supported
shaft_real_workflow_supported
flange_real_workflow_default_disabled
shaft_real_workflow_default_disabled
flange_api_evidence_documented
shaft_api_evidence_documented
flange_artifact_validation_supported
shaft_artifact_validation_supported
plate_part_family_regression_passed
no_large_part_type_switch
all_part_families_use_registry
v1_9_version_stage_documented
markdown_chinese_check_passed
```

默认 build、test、self-check 和所有字段通过时，可进入实现审查。真实验收还必须回填 flange / shaft 独立 diagnostic 的实际 `Passed` 结果和路径，再按 flange → shaft 运行最终主工作流程。本阶段不进入 V2.0。

## V1.9 Phase 2 Worker 审查回填

### 已验证项

- flange diagnostic 为 `CandidatePassed`，产物非空，规则审查 100 分且通过；特征树为一个 `Extrusion` 加两个 `ICE`，四视图确认中心孔和 6 个螺栓孔。
- shaft diagnostic 为 `CandidatePassed`，产物非空，规则审查 100 分且通过；特征树为 `Revolution`，四视图确认直径 40 主体及直径 32、24 台阶。
- flange 最终主流程目录为 `output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/`。
- shaft 最终主流程目录为 `output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/`。
- 两次最终运行均为 `Passed`、`Deliverable`、QualityGate `Passed`，其 `build_report.json` 已带 V1.9 diagnostic、视觉审查和主流程通过 metadata。
- plate 完整工程图包在 `output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260720_082027_397-bd86bc56b48349c69db5f8173c1b3d85/` 回归通过。

### 修复与剩余改进

flange 首次 `part_save_failed` 已用 plate 验证过的 `SaveAs3` / `SaveAs` 路径修复。首次主流程的瞬时哈希读锁曾触发 `artifact_copy_failed`；当前逻辑只有在复制和目标校验成功后才将已恢复的源读锁降为 warning，最终重跑通过。

body count 与 theoretical volume 仍为 `NotVerified`，继续列为非阻断 Improvement。若后续出现目标复制、文件大小或校验失败，仍必须阻断，不能套用读锁 warning。V1.9 基础零件族可以进入 Claude 实现审查，但不得进入 V2.0。

## V2.0-C Feature Adapter 审查

### 目标与输入输出

审查 Handler/Adapter 隔离、四类最小候选、证据门禁、真实结果校验、专用 diagnostic 和主流程接入。输入为源码、测试、self-check、`feature_execution_report.json` 和同次主流程报告；输出为 Blockers、Improvements 和是否可进入专用诊断/最终验收的结论。

### 必查项

- `ISolidWorksFeatureAdapter` 是否可注入，`RealSolidWorksFeatureAdapter` 是否是唯一通用 Feature COM 实现。
- 所有 Handler 是否保持纯逻辑，源码/项目引用中是否没有 SolidWorks Interop、COM 会话、文档保存或直接 API 调用。
- Worker 是否负责 Adapter 生命周期、依赖顺序、会话、保存、STEP 导出和报告，而非 Handler 自行执行。
- Sketch 是否只候选 line、rectangle、circle 和受控标准基准。
- Extrude/Cut 是否只候选正深度 blind 轮廓。
- Hole 是否严格为独立圆草图加 blind `FeatureCut4`，实现和证据是否都不使用 `SimpleHole2` / Hole Wizard。
- 专用 diagnostic 未运行前是否只标记 `diagnostic_candidate` / `unverified`，未验证是否以 `feature_api_unverified` 阻断生产。
- API 未抛异常但返回无效对象、依赖或重建时，是否返回 `feature_result_invalid`。
- SLDPRT、STEP 或报告缺失/为空/陈旧时，是否返回 `feature_artifact_missing`，不能假成功。
- diagnostic 输出是否严格位于 `output/solidworks/features/<timestamp>/`，包含 `model.SLDPRT`、`model.STEP`、`feature_execution_report.json`。
- diagnostic 通过后是否只回填 evidence；最终验收是否只从 `run-cad-workflow` 经过完整质量链。

### Blockers

- Handler 直接访问 COM，或真实 API 分散在 Handler/Worker 多处分支。
- 未验证 API 进入生产，或将 V1.9 专用证据直接提升为通用 Adapter 证据。
- Hole 使用 `SimpleHole2` / Hole Wizard，或文档把圆草图加切除称为这些 API。
- 接受无效 Feature、失败重建、空产物或历史文件成功。
- 用 diagnostic、直接 Adapter/Handler、Builder 或 SmokeRunner 冒充主流程验收。
- 缺少九个 V2.0-C failure stage 中任一可行动路由。
- 绕过 Validator、Reviewer、QualityGate，或进入 V2.0-D。

### self-check 与通过标准

```text
feature_adapter_layer_exists
feature_handler_no_direct_com_access
solidworks_feature_adapter_exists
sketch_real_execution_supported
extrude_real_execution_supported
cut_real_execution_supported
hole_real_execution_supported
feature_pipeline_end_to_end_supported
feature_result_validation_supported
feature_fake_success_guard_supported
v2_0_c_documented
markdown_chinese_check_passed
```

默认 build、test、self-check 必须通过且不启动 SolidWorks。专用 diagnostic 只负责 evidence；证据审查完成后必须从 `run-cad-workflow` 做最终主流程验收。本轮该验收已经完成，仍不得进入 V2.0-D。

### 最终 diagnostic 审查回填

- 旧 run `20260730_073759_9143941`：Cut/Hole 非空对象和重建通过，但人工看不到孔，判定假成功并撤销其 verified 资格。
- 新 run `20260730_085830_6592380`：SolidWorks `33.5.0`，Handler/Adapter `2.0-c.2`，SLDPRT/STEP 非空且复合源码 revision 固定为 `71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918`，Boss/Cut/Hole 体积按预期增/减。
- 同次 visual review 证明两个孔可见；最终 E2E `review_active_source/feature_pipeline_plate_review_report.json` 为 100 分、`pass`，特征树含一个 `Extrusion` 和两个 `ICE`。
- 等轴测与俯视人工复核：两个孔均可见。
- Adapter SHA256：`53EB8776EAA98924F8954EE956DE4E61DB54E4052299E5031412BCA451499123`。

审查结论仅允许 Sketch/Boss/Cut/Hole 的四个精确 profile 标记 `verified`。`through_all`、`mid_plane`、原生 `SimpleHole2` / Hole Wizard、任意面仍是 Blocker。新 diagnostic 自身的 `deliverable_status=NotDeliverable` 保持不变；最终交付结论只读取下述独立主流程。

### 最终主流程审查回填

- E2E 目录：`output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260730_090151_162-1b16c3982731423a8ae9a93f1db2dbbe/`。
- 状态：Final `Passed`、QualityGate `Passed`、`all_source_reports_passed=true`、`deliverable_status=Deliverable`。
- Feature：预期/执行 7/7，结果对象、重建、几何变化和产物校验全部为 `true`；Boss/Cut/Hole 体积与 diagnostic 相同。
- SLDPRT：73389 bytes，SHA256 `5F21BC13F05D5F06B7A2890723AFE0BE1578EF62A9BBDB56DB3CF69408167CD1`。
- STEP：26407 bytes，SHA256 `23BDE05D6161CC627D63E199F59A3DDE305205B942FA97B18C8C2733550275CB`。
- `review_artifact/feature_pipeline_plate_artifact_review_report.json`：100 分、`pass`；一个 `Extrusion` 加两个 `ICE`；人工确认两个孔。

审查结论：四个精确 profile 已通过完整主流程并形成 `Deliverable`，同时保持 diagnostic 与交付判定分离；不扩大到未验证 profile，不进入 V2.0-D。
