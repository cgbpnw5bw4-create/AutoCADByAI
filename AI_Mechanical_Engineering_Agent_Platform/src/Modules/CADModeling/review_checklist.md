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
- V1.7 CLI 绕过 Gateway 或 `chief-engineer` 直接调用 Worker、Builder 或 SmokeRunner。
- V1.7 未解析 `build_complete_drawing_package` 与 `part_type=plate_basic_4holes`，却用自然语言静默触发真实 CAD。
- V1.7 任一真实确认缺失时回退 Fake Worker 后返回 Passed、Deliverable 或 `real_cad_executed=true`。
- V1.7 发布包通过历史 latest 或 SmokeRunner 报告拼接不同运行的源文件。
- V1.7 任一源报告失败、真实执行证据不足或总体 QualityGate 未通过时仍标记 Deliverable。
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

必须通过 build、test、self-check。涉及真实 CAD 的能力必须支持 V2.0 统一禁用策略、诊断 Runner、artifact validation 和 QualityGate 记录。

V1.2 还必须确认 `solidworks_real_drawing_dimensions_implemented`、`solidworks_real_drawing_dimensions_not_called_in_default_self_check`、`solidworks_drawing_dimension_failure_stage_actionable`、`solidworks_drawing_dimension_api_evidence_documented` 和 `solidworks_drawing_dimension_failure_repair_documented` 为 true。

V1.3 还必须确认 `solidworks_real_drawing_title_block_implemented`、`solidworks_real_drawing_title_block_not_called_in_default_self_check`、`solidworks_drawing_title_block_failure_stage_actionable`、`solidworks_drawing_title_block_api_evidence_documented`、`solidworks_drawing_title_block_failure_repair_documented` 和 `v1_3_version_stage_documented` 为 true。

V1.4 还必须确认 `solidworks_release_package_implemented`、`solidworks_release_package_default_no_cad_execution`、`solidworks_release_manifest_generated`、`solidworks_package_quality_report_generated`、`solidworks_release_summary_generated`、`solidworks_release_package_failure_stage_actionable`、`solidworks_release_package_failure_repair_documented` 和 `v1_4_version_stage_documented` 为 true。

V1.5 还必须确认 `real_cad_worker_integrated_into_main_workflow`、`chief_engineer_orchestrator_invokes_cad_workflow`、`workflow_engine_can_route_to_solidworks_worker`、`real_cad_main_workflow_default_disabled`、`real_cad_main_workflow_requires_request_flag`、`real_cad_main_workflow_requires_env_flag`、`real_cad_main_workflow_passes_quality_gate`、`release_package_all_source_reports_passed_field_exists`、`release_package_deliverable_status_field_exists`、`release_package_failed_source_reports_block_deliverable` 和 `v1_5_version_stage_documented` 为 true。

V1.7 还必须确认全部 `real_cad_e2e_*` 字段、`v1_7_version_stage_documented` 与 `markdown_chinese_check_passed` 为 true。真实验收报告必须记录 Gateway、ChiefEngineerOrchestrator、WorkflowEngine、Router、RealWorker、连接、总体 QualityGate、源报告与可交付语义。标题栏只能表述为自定义属性写入、读回和刷新，不得声称 Sheet Format 可见渲染已验收。

## V1.8 审查目标与适用范围

V1.8 审查结构化输入到发布包的通用零件族执行链，以及 `plate_basic_4holes`、`flange_basic`、`shaft_basic` 三个独立定义。输入为源码、测试、self-check 报告和 API evidence；输出为 Blockers、Improvements、测试结果与是否可进入 Claude 审查的结论。

## V1.8 必查执行链

- 是否使用 `CADModelSpec` 的 `part_type`、`dimensions`、`features`、`material`、`output_requirements`、`drawing_requirements`、`execution_options` 通用边界。
- 是否按 `CADModelSpec` → `PartTypeRegistry` →参数 Validator → BuildPlan → `PartFamilyBuilder` → Worker → ArtifactValidator → Drawing → QualityGate → ReleasePackage 执行。
- 每个零件族是否独立包含 Schema、Validator、BuildPlan 生成逻辑、`PartFamilyBuilder`、`failure_stage`、API evidence 和测试。
- 真实执行是否仍通过 `ChiefEngineerOrchestrator` → `WorkflowEngine` → Router → Worker → Validator → Reviewer → `QualityGate`，没有新增直连入口。
- `unsupported_part_type`、`missing_required_parameter` 和 `invalid_parameter_value` 是否在 Worker 之前停止，并有未调用 Worker/未连接 SolidWorks 的行为测试。
- 是否没有在 Router、Skill、Worker 或 Builder 中新增大型 `switch(part_type)`；零件族分发是否由 Registry 和多态定义完成。
- `plate_basic_4holes` 已有真实建模、工程图、QualityGate 和发布包能力是否保持，旧回归测试是否全部通过。
- `flange_basic` 是否覆盖六个必需参数及径向几何约束，`shaft_basic` 是否覆盖基本直径/长度和可选台阶数组成对约束。
- `flange_basic` 与 `shaft_basic` 本轮是否只声称 dry-run 通过，没有把未完成的真实 smoke 说成真实验收。

## V1.8 Blockers

- 未注册 `part_type` 静默回退为 `plate_basic_4holes`。
- 非法零件参数进入 Fake 或 Real Worker，或默认 self-check 启动 SolidWorks。
- 用大型 `switch(part_type)` 或多处 `if (part_type == ...)` 替代 `PartTypeRegistry`。
- 任一零件族缺少 Schema、Validator、BuildPlan、Builder、`failure_stage`、API evidence 或测试。
- `plate_basic_4holes` 回归失败，或 `flange_basic` / `shaft_basic` dry-run 失败。
- 以 `CreateCircle` / `FeatureExtrusion2` / `FeatureCut4` 候选或 `CreateLine` / `CreateCenterLine` / `FeatureRevolve2` 候选直接声称真实验收通过。
- 越界实现装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9。

## V1.8 验证步骤与进入 Claude 审查条件

1. 运行 `dotnet build AI_Mechanical_Engineering_Agent_Platform.sln`。
2. 运行 `dotnet test`。
3. 在未开启任何真实 CAD 环境开关时运行 `dotnet run --project src/Interfaces/CliHost -- self-check`。
4. 确认以下 self-check 字段全部为 `true`：

   - `generic_cad_model_spec_supported`
   - `part_type_registry_exists`
   - `plate_part_family_registered`
   - `flange_part_family_registered`
   - `shaft_part_family_registered`
   - `unsupported_part_type_rejected`
   - `invalid_part_parameters_rejected_before_worker`
   - `part_family_builders_do_not_use_large_switch`
   - `plate_regression_passed`
   - `flange_dry_run_passed`
   - `shaft_dry_run_passed`
   - `real_cad_part_family_default_disabled`
   - `v1_8_version_stage_documented`
   - `markdown_chinese_check_passed`

上述条件全部满足时可进入 Claude 实现审查。`flange_basic` 只有完成独立真实 smoke 后才可进入真实验收；`shaft_basic` 还必须先获得 `CreateLine`、`CreateCenterLine`、`FeatureRevolve2` 的专用诊断证据。本轮审查结论不得授权进入 V1.9。

## V1.9 Phase 1 审查合同

### 目标与输入输出

审查 `flange_basic` 和 `shaft_basic` 的真实 build-only 主工作流程，同时保护 `plate_basic_4holes` 完整工程图包回归。输入为源码、测试、默认 self-check、API evidence、同次真实报告与发布包；输出为 Blockers、Improvements、合同通过状态和待回填证据。

### 必查项

- 最终路径是否严格经过 Gateway / `chief-engineer`、`ChiefEngineerOrchestrator`、`WorkflowEngine`、`SolidWorksWorkflowRouter`、两个 Registry、`RealSolidWorksWorker`、ArtifactValidator、Reviewer、QualityGate 和 build-only ReleasePackage。
- flange / shaft 是否不自动生成 Drawing，plate 是否保持完整图包回归。
- 是否使用 V2.0 统一执行策略，且 self-check、CI、单元测试和 dry-run 不连接 COM。
- 真实 SolidWorks 是否全局串行，验收是否按 flange → shaft 顺序。
- 两族包是否写入 `output/solidworks/e2e/<part_type>/<timestamp>/`，并包含 SLDPRT、STEP、`build_report.json`、`e2e_execution_report.json`、`release_manifest.json`。
- 是否使用全部十二个专用 `failure_stage`，且 API evidence 不足时失败关闭。
- 法兰是否使用外圆拉伸、独立内孔草图切除、单草图螺栓孔切除，并拒绝 `HoleWizard` / 圆周阵列。
- 轴是否使用 `CreateLine`、`CreateCenterLine`、selection mark `16` 和 `FeatureRevolve2` 360°，没有混用偏移多段拉伸。
- 是否仅声称当前证据足以进入 diagnostic，并把实际 smoke 结果/路径保留为待回填。
- 最终验收是否禁止直接 Builder / SmokeRunner、历史 latest 和仅文件存在证据。

### Blockers

- flange / shaft 自动进入工程图，或 plate 完整图包回归退化。
- 禁用策略生效后仍连接 COM，或真实任务并发。
- 绕过 Gateway、Agent、WorkflowEngine、Registry、Worker、Validator、Reviewer 或 QualityGate。
- 发布包缺少任一必需产物/报告，却被标记可交付。
- API evidence 不足却声称真实建模已 `Passed`，或伪造 smoke 路径。
- 使用大型 `switch(part_type)`、未经 Registry 分发，或进入 V2.0。

### 自检与通过标准

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

默认 build、test、self-check 和上述字段全部通过后，可进入实现审查。真实验收仍需回填 flange / shaft 独立 smoke 的实际结果、报告路径和同次主工作流程包。本阶段不进入 V2.0。

## V1.9 Phase 2 审查回填

Phase 1 的 diagnostic 与主流程待回填项已经关闭：

- `flange_basic` diagnostic 为 `CandidatePassed`，规则审查 100 分且通过；特征树和四视图确认中心孔及 6 个螺栓孔。
- `shaft_basic` diagnostic 为 `CandidatePassed`，规则审查 100 分且通过；特征树和四视图确认旋转体及直径 40、32、24 的两级台阶。
- flange 最终目录为 `output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/`。
- shaft 最终目录为 `output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/`。
- 两次主流程均为 `Passed`、`Deliverable`、QualityGate `Passed`，且最终构建报告已回填 diagnostic、视觉复核和主流程证据。
- plate 完整回归目录为 `output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260720_082027_397-bd86bc56b48349c69db5f8173c1b3d85/`，状态同为 `Passed`、`Deliverable`、QualityGate `Passed`。

因此，V1.9 基础法兰、基础轴 build-only 真实验收和 plate 完整包回归均有同次证据，可进入 Claude 实现审查。diagnostic 的 body count 与 theoretical volume 仍为 `NotVerified`，按 Improvement 处理，不阻断本阶段；禁止据此扩大到自动工程图或 V2.0。

## V2.0-A 通用 CADModelSpec 审查

### 必查项

- canonical `CADModelSpec` 是否包含 11 个核心字段，并受控兼容旧 `id`、`part_type`、`dimensions` 和对象形式 `features`。
- `SketchDefinition` 是否包含实体、约束、尺寸、基准引用和稳定标识；约束是否只引用同草图已存在实体。
- `FeatureDefinition` 是否覆盖十类特征，并包含依赖、草图/特征引用、执行顺序和目标引用。
- `FeatureGraph` 是否拒绝缺失依赖、依赖环、非正/重复/依赖逆序，并产生稳定拓扑顺序。
- `BuildPlanCompiler` 是否从图生成草图、中心线、特征、保存与 STEP 导出 operation，且不连接 COM。
- plate、flange、shaft 是否都通过 `PartFamilyGenericModelFactory` 和 `BuildPlanCompiler`，没有第二套手写计划。
- 图或编译失败是否在 Worker 前结束，并保留精确 `failure_stage`。
- `RealSolidWorksWorker` 是否只经 Registry 解析 V1.9 专用 Builder，没有三族字符串分支。
- 文档和报告是否明确十类 operation 映射不等于通用真实 Feature Handler。

### Blockers

- 从零件族参数直接手写 BuildPlan operation，或 FeatureGraph 不再是唯一来源。
- 缺失依赖、环或非法顺序仍调度 Worker。
- 用输入数组顺序覆盖拓扑结果，或静默删除依赖。
- 把 V1.9 三族真实 Builder 证据泛化为任意 FeatureGraph 已真实执行。
- 在 V2.0-A 新增通用 COM Handler、装配体、BOM、批量队列或进入 V2.0-B。

### 自检字段

```text
generic_cad_model_spec_v2_supported
sketch_definition_supported
sketch_constraints_supported
feature_definition_supported
feature_graph_supported
feature_graph_cycle_detected
missing_feature_dependency_rejected
build_plan_compiler_supported
plate_uses_generic_feature_graph
flange_uses_generic_feature_graph
shaft_uses_generic_feature_graph
no_part_specific_logic_in_real_worker
v2_0_a_documented
markdown_chinese_check_passed
```

build、test、默认 self-check 和上述字段全部通过后，V2.0-A 才可进入实现审查。self-check、单元测试与 dry-run 不得连接 COM；本轮不得运行真实 CAD，也不得进入 V2.0-B。

## V2.0-D 模型重建与几何验证审查

### Blockers

- 参数变更未完整经过 `CADModelSpec` → `ModelUpdateService` → `BuildPlanCompiler` → `FeatureExecutionPipeline` → `SolidWorks Rebuild`，或通过构建器、直接 `COM`、未注册处理器绕过该路径。
- `ModelUpdateService`、`GeometryValidator`、报告或 `QualityGate` 层直接持有 `COM`；真实读取未收敛到独立 `GeometryReader`。
- 未记录 `old_parameters`、`new_parameters`、`changed_features`、`rebuild_result`，或者 `FeatureGraph` 的节点、依赖、排序在参数更新后被破坏。
- `GeometryValidator` 未从真实 `SolidWorks` 输出验证包围盒、实体数量、体积、可用质量属性、草图/拉伸/切除/孔，以及参数到实际长度、孔径/轴径的映射。
- 将 `COM` 返回成功、`SLDPRT`/`STEP` 存在、历史产物或诊断结果当作 `GeometryValidator` / `QualityGate` 成功。
- 未生成、未解析或未纳入同次 `QualityGate` 的 `geometry_validation_report.json` 与 `rebuild_report.json`。
- 未以真实几何逐孔证明 `plate_basic_4holes` 的四孔，或以新增 CAD Feature、零件族、阵列、贯穿、对称拉伸或孔向导绕过既有受证 Feature 类型。
- 未将 `rebuild_failed`、`geometry_read_failed`、`bounding_box_invalid`、`volume_validation_failed`、`parameter_geometry_mismatch`、`feature_missing_after_rebuild`、`geometry_report_failed` 失败关闭。
- 真实验收不是 `run-cad-workflow --input examples/parameter_update_plate.json`，或试图进入 V2.0-E。

### 必须回填的自检项

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

只有第一次 160×80×12 与更新后的 200×100×15 都经同次真实 `GeometryValidator`、`Reviewer` 和 `QualityGate` 通过，且输出 `SLDPRT`、`STEP`、`geometry_validation_report.json`、`rebuild_report.json`，本阶段才可关闭；不得进入 V2.0-E。

## V2.1-A 夹套复核

- `jacket_basic` 已注册独立参数 Schema 和 Validator，且 `inner_diameter_mm < outer_diameter_mm`、单边壁厚至少 1 mm。
- FeatureGraph 与 BuildPlan 顺序为 TopPlane 外圆草图、盲拉伸、TopPlane 内圆草图、两倍轴向长度盲切，特征标识为 `jacket_body_extrude`、`jacket_inner_cut`。
- dry-run 通过注册表解析；结构化生产证据未激活时，真实 Worker 必须在 COM 连接前拒绝，CLI 没有直接调用 Builder。
- 真实主流程的 SLDPRT、物理内容有效的 STEP、实测几何、构建报告、Reviewer、QualityGate 与 ReleasePackage 同次通过后才可交付。
