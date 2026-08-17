# CADModeling 执行说明

## 目标

`CADModeling` 模块负责把结构化需求转换为可执行的 CAD 建模计划，并通过受控 Worker 生成或验证产物。V1.8 把原先围绕 `plate_basic_4holes` 的入口升级为参数化零件族边界；`plate_basic_4holes` 保持已有真实能力，`flange_basic` 与 `shaft_basic` 本轮只承诺 Schema、校验、BuildPlan 和 dry-run。

## 标准链路

```text
结构化输入
→ CADModelSpec
→ PartTypeRegistry
→ 零件族参数 Validator
→ BuildPlan
→ Router
→ FakeSolidWorksWorker 或 RealSolidWorksWorker
  → PartFamilyBuilderRegistry
  → 对应 PartFamilyBuilder
→ SolidWorksArtifactValidator
→ Drawing
→ Reviewer
→ QualityGate
→ ReleasePackage
```

## 职责边界

- `Skill` 只生成 `SolidWorksBuildPlan`，不调用 Worker。
- `Worker` 才执行 dry-run 或真实 CAD 操作。
- `Validator` 检查输入、环境和输出产物。
- `Reviewer` 做工程合理性复审。
- `QualityGate` 负责最终裁决。
- `Agent` 不直接调用 Worker。
- `Gateway` 不直接调用 Worker。
- `LLM` 不直接调用 Worker。

## 安全开关

V2.0 起，本地交互式 `SolidWorks` 主流程默认真实执行：`dry_run=false`、`SW_VISIBLE=true`。`dry_run=true`、`SW_DISABLE_REAL_EXECUTION=true`、CI、单元测试或 `SW_FORCE_FAKE_WORKER=true` 时禁止进入真实连接路径。

## 主流程触发输入

CAD 主流程接受显式 `solidworks_main_workflow`、共享状态中的结构化 `CADModelSpec`、显式 `part_type` / `cad_model_type`，或明确包含已注册零件族名称的受控请求。仅泛化提到 `SolidWorks` 不构成触发条件。显式结构化但未注册的类型必须进入受控流程并在 Worker 前返回 `unsupported_part_type`；任何未知类型都不得静默改写为 `plate_basic_4holes` 或启动真实 CAD。

## self-check 字段

本模块至少关注：

- `solidworks_module_skeleton_enabled`
- `solidworks_build_plan_skill_registered`
- `solidworks_build_plan_generated`
- `solidworks_build_plan_validator_passed`
- `solidworks_artifact_validator_passed`
- `solidworks_build_plan_reviewer_passed`
- `solidworks_quality_gate_passed`
- `solidworks_api_repair_loop_available`
- `solidworks_real_drawing_dimensions_implemented`
- `solidworks_real_drawing_dimensions_not_called_in_default_self_check`
- `solidworks_drawing_dimension_failure_stage_actionable`
- `solidworks_real_drawing_title_block_implemented`
- `solidworks_real_drawing_title_block_not_called_in_default_self_check`
- `solidworks_drawing_title_block_failure_stage_actionable`
- `solidworks_release_package_implemented`
- `solidworks_release_package_default_no_cad_execution`
- `solidworks_release_manifest_generated`
- `solidworks_package_quality_report_generated`
- `solidworks_release_summary_generated`
- `solidworks_release_package_failure_stage_actionable`
- `real_cad_worker_integrated_into_main_workflow`
- `chief_engineer_orchestrator_invokes_cad_workflow`
- `workflow_engine_can_route_to_solidworks_worker`
- `real_cad_main_workflow_default_disabled`
- `real_cad_main_workflow_requires_request_flag`
- `real_cad_main_workflow_requires_env_flag`
- `real_cad_main_workflow_passes_quality_gate`
- `release_package_all_source_reports_passed_field_exists`
- `release_package_deliverable_status_field_exists`
- `release_package_failed_source_reports_block_deliverable`
- `v1_5_version_stage_documented`
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

## 成功标准

默认 self-check 必须 Passed，且不能启动真实 CAD。涉及真实 API 的修复必须先在诊断 Runner 中验证，再回填 Worker。

## V1.1 工程图补充

V1.1 只在真实零件已经存在时，从 `plate_basic_4holes.SLDPRT` 生成基础工程图。该能力仍属于 Worker 层，`Agent`、`Gateway` 和 `LLM` 不能直接调用。

```text
plate_basic_4holes.SLDPRT
→ SolidWorksDrawingBuilder
→ Front / Top / Right / Isometric 基础视图
→ plate_basic_4holes.SLDDRW
→ plate_basic_4holes.pdf
→ drawing_report.json
→ SolidWorksArtifactValidator
```

self-check 不执行真实工程图。需要隔离诊断时使用独立工程图 Runner；最终验收仍从 CLI 主入口运行。

## V1.2 工程图尺寸补充

V1.2 只在 V1.1 工程图已经存在时，从 `plate_basic_4holes.SLDDRW` 生成带基础尺寸的工程图。该能力仍属于 Worker 层，`Agent`、`Gateway` 和 `LLM` 不能直接调用。

```text
plate_basic_4holes.SLDDRW
→ SolidWorksDrawingDimensionBuilder
→ 确认 Front / Top / Right / Isometric
→ 160 mm 长度、80 mm 宽度、12 mm 厚度、Φ10 孔径、孔中心距
→ plate_basic_4holes_dimensioned.SLDDRW
→ plate_basic_4holes_dimensioned.pdf
→ dimension_report.json
→ SolidWorksArtifactValidator
```

self-check 不执行真实尺寸标注。真实尺寸诊断使用独立 Runner，严格诊断可使用其专用严格模式；这些入口不能替代主流程验收。

## V1.3 工程图标题栏补充

V1.3 只在 V1.2 带尺寸工程图已经存在时，从 `plate_basic_4holes_dimensioned.SLDDRW` 生成带最小标题栏信息的工程图。该能力仍属于 Worker 层，`Agent`、`Gateway` 和 `LLM` 不能直接调用。

```text
plate_basic_4holes_dimensioned.SLDDRW
→ SolidWorksDrawingTitleBlockBuilder
→ 读取当前 Sheet 和比例
→ 写入 PartName、DrawingNumber、Material、Scale、DrawingDate、Revision
→ plate_basic_4holes_title_block.SLDDRW
→ plate_basic_4holes_title_block.pdf
→ title_block_report.json
→ SolidWorksArtifactValidator
```

self-check 不执行真实标题栏测试。真实标题栏诊断使用独立 Runner，严格诊断可使用其专用严格模式；这些入口不能替代主流程验收。

## V1.4 工程发布包补充

V1.4 只在 V1.0-B 到 V1.3 的真实输出已经存在时，将 `plate_basic_4holes` 相关文件整理成发布包。该能力属于 Worker 层文件收集和质量检查，`Agent`、`Gateway` 和 `LLM` 不能直接调用，也不能借机启动 SolidWorks。

```text
V1.0-B / V1.1 / V1.2 / V1.3 真实输出
→ SolidWorksReleasePackageBuilder
→ artifacts/plate_basic_4holes.SLDPRT
→ artifacts/plate_basic_4holes.STEP
→ artifacts/plate_basic_4holes.SLDDRW
→ artifacts/plate_basic_4holes.pdf
→ reports/*.json
→ release_manifest.json
→ package_quality_report.json
→ release_summary.md
```

默认 self-check 可运行 V1.4 发布包检查，因为它只读写文件系统，不执行 CAD。若源 SLDDRW、PDF 或报告缺失，发布包必须保持 Failed，并输出 `source_artifacts_missing` 或 `source_report_missing`，不得伪造真实工程图产物。

## V1.5 主工作流集成补充

V1.5 不新增 CAD 子功能，只把 V1.0-B 到 V1.4 已有的真实 SolidWorks 能力接入主工作流。主流程由 `ChiefEngineerOrchestrator` 委托 `SolidWorksWorkflowRouter` 识别显式 flag、结构化上下文或具体 `plate_basic_4holes` 请求后触发；泛化提到 `SolidWorks` 不应单独触发 CAD 主流程。主流程仍必须经过 `SequentialWorkflowEngine`、`SolidWorksBuildPlanSkill`、`SolidWorksBuildPlanValidator`、Worker、`SolidWorksArtifactValidator`、`SolidWorksBuildPlanReviewer` 和 `QualityGate`。

```text
ChiefEngineerOrchestrator
→ SequentialWorkflowEngine 内部 Agent 协作
→ SolidWorksWorkflowRouter
→ SolidWorksMainWorkflowRunner
→ SolidWorksBuildPlanSkill
→ SolidWorksBuildPlanValidator
→ FakeSolidWorksWorker 或 RealSolidWorksWorker
→ SolidWorksArtifactValidator
→ SolidWorksBuildPlanReviewer
→ QualityGate
→ AgentOutput 返回 real_cad_executed、quality_gate_passed 和 artifact 路径
```

本地交互式主流程默认选择 `RealSolidWorksWorker`，不再要求请求确认或启用环境变量。dry-run、CI、单元测试、`SW_DISABLE_REAL_EXECUTION=true` 或 `SW_FORCE_FAKE_WORKER=true` 时选择 `FakeSolidWorksWorker`。`Agent`、`Gateway` 和 `LLM` 仍不能直接调用 Worker。

V1.5 还修正发布包语义：`package_build_status` 只表示打包过程是否成功，`all_source_reports_passed` 表示所有源报告是否通过，`deliverable_status` 表示最终是否可交付。任一源报告 `final_status=Failed` 时，`source_report_failures` 必须列出失败报告，`all_source_reports_passed=false`，`deliverable_status=NotDeliverable`。

## V1.7 主工作流端到端验收补充

V1.7 只串联已有能力，不新增 CAD 功能。CLI 结构化输入的 `operation=build_complete_drawing_package` 与 `part_type=plate_basic_4holes` 由 Gateway 交给 `chief-engineer`。主链路为：

```text
Gateway
→ ChiefEngineerOrchestrator
→ SequentialWorkflowEngine
→ SolidWorksWorkflowRouter
→ RealSolidWorksWorker
→ Build / Drawing / Dimension / TitleBlock
→ ArtifactValidator / Reviewer / QualityGate
→ 显式同次 source set
→ ReleasePackage
→ 总体 QualityGate
```

四阶段源文件必须使用同一 request 的精确绝对路径传递，不得从历史 latest 目录拼接。最终包写入 `output/solidworks/e2e/plate_basic_4holes/<timestamp>/`；阶段产物保持在既有 `output/solidworks/real/` 受信任根。策略明确禁用真实执行时不得把 Fake Worker 结果当作真实验收结果。

## V1.7-REAL-AUTH 本地开发授权

本地开发人员可在不提交的 `config/solidworks.local.json` 中提供模板、可见性和超时。CLI 默认形成 `dry_run=false` 的本地交互请求；结构化业务输入只描述零件和交付内容，不承担授权字段。

该便利不改变产品边界：配置不存在不阻止默认本地执行，但模板缺失仍会在 preflight 明确失败；Gateway、Agent 和 LLM 仍不能直接访问 Worker；完整流程仍必须穿过 WorkflowEngine、ArtifactValidator、Reviewer、QualityGate 与 ReleasePackage。self-check 不启动 SolidWorks。

## V1.8 参数化零件族

### 目标与适用范围

V1.8 通过 `CADModelSpec`、`PartTypeRegistry` 和每族独立定义，使新增零件族不需要在 Router、Skill 或 Worker 内继续堆叠大型 `switch(part_type)`。本轮只支持 `plate_basic_4holes`、`flange_basic` 和 `shaft_basic`。

### 通用输入与输出

`CADModelSpec` 至少包含以下通用字段：

- `part_type`：在 `PartTypeRegistry` 中查找的稳定零件族标识。
- `dimensions`：长度、直径、厚度等数值参数。
- `features`：孔阵列、轴台阶等特征参数。
- `material`：材料标识及可审计属性。
- `output_requirements`：SLDPRT、STEP、报告和发布包要求。
- `drawing_requirements`：是否生成工程图及最小出图要求。
- `execution_options`：dry-run、真实执行授权和输出路径等选项。

输出为通过零件族 Validator 的 BuildPlan、Worker 产物、工程图、证据报告、QualityGate 裁决与 ReleasePackage。

### 首批零件族参数

| `part_type` | 必需参数 | 可选参数 | 本轮能力边界 |
|---|---|---|---|
| `plate_basic_4holes` | `length_mm`、`width_mm`、`thickness_mm`、`hole_count`、`hole_diameter_mm` | 孔位或矩形阵列特征参数 | 保持四孔板已有真实建模和完整交付回归能力。 |
| `flange_basic` | `outer_diameter_mm`、`inner_diameter_mm`、`thickness_mm`、`bolt_hole_count`、`bolt_hole_diameter_mm`、`bolt_circle_diameter_mm` | 无 | 完成独立 Schema、Validator、BuildPlan、Builder 和 dry-run；不声称真实验收。 |
| `shaft_basic` | `diameter_mm`、`length_mm` | `optional_step_diameters`、`optional_step_lengths` | 完成独立 Schema、Validator、BuildPlan、Builder 和 dry-run；不做键槽、螺纹或其他复杂轴特征。 |

`optional_step_diameters` 与 `optional_step_lengths` 必须同时缺省，或同时提供且元素数量一致。所有毫米参数必须为有限正数；法兰内径必须小于外径，螺栓孔和分布圆必须落在法兰可用径向区间内。

### 执行步骤

1. 先由通用 `CADModelSpecValidator` 检查基本字段和 `part_type`。
2. `PartTypeRegistry` 查找独立的 `IPartFamilyDefinition`；未注册时返回 `unsupported_part_type`。
3. 零件族 Validator 检查必需参数、数值范围和参数间约束。
4. 定义对象生成该零件族的 BuildPlan，并解析对应 `IPartFamilyBuilder`。
5. dry-run 产生可审计的模拟产物；真实执行仍必须通过 `ChiefEngineerOrchestrator` → `WorkflowEngine` → Router → Worker → Validator → Reviewer → `QualityGate`。
6. 通过 ArtifactValidator 后才能进入 Drawing 与 ReleasePackage；未通过不得标记可交付。

### 前置失败与验证标准

- 未注册类型必须以 `unsupported_part_type` 停在 Worker 之前。
- 缺少必需参数返回 `missing_required_parameter`，非法数值或关系返回 `invalid_parameter_value`，两者都不得连接 SolidWorks。
- 定义、计划生成或 Builder 缺失分别使用 `part_family_definition_missing`、`build_plan_generation_failed`、`part_family_builder_missing`。
- 必须通过 `plate_basic_4holes` 回归、`flange_basic` dry-run、`shaft_basic` dry-run，并保留 `flange_build_failed`、`shaft_build_failed`、`artifact_validation_failed` 的可行动路由。
- 默认 self-check 只检查注册、校验、计划、dry-run 和源码结构，不启动 SolidWorks。

V1.8 进入 Claude 审查前，用户指定的十四个 self-check 字段必须全部为 `true`，且 build、test、self-check 全部通过。`flange_basic` 和 `shaft_basic` 只凭 dry-run 不得进入真实可交付判定。

### 禁止事项

- 禁止以大型 `switch(part_type)` 或分散的 `plate_basic_4holes` 特判代替 Registry 多态分发。
- 禁止非法参数进入真实 Worker，禁止默认 self-check 启动 SolidWorks。
- 禁止在本轮实现装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9。

## V1.9 Phase 1 真实 build-only 执行

### 目标与范围

`flange_basic` 和 `shaft_basic` 在本阶段只执行真实零件构建、保存、STEP 导出、产物校验、工程复审、质量门禁和 build-only 发布包。两族不生成 Drawing；`plate_basic_4holes` 继续保持原完整 Drawing / Dimension / TitleBlock / ReleasePackage 回归。

### 执行链

```text
结构化输入
→ Gateway / chief-engineer
→ ChiefEngineerOrchestrator
→ WorkflowEngine
→ SolidWorksWorkflowRouter
→ PartTypeRegistry
→ PartFamilyBuilderRegistry
→ RealSolidWorksWorker
→ ArtifactValidator
→ Reviewer
→ QualityGate
→ build-only ReleasePackage
```

Router 根据已注册 `part_type` 选择 build-only 或 plate 完整包语义。最终验收不允许 CLI、Agent 或审查者直接调用 Builder / SmokeRunner。

### 授权、串行和输出

真实执行由 V2.0 统一运行策略决定，不再经过请求确认或本地授权层。self-check、CI、单元测试和 dry-run 不连接 COM；真实任务仍全局串行。

两族输出根目录为 `output/solidworks/e2e/<part_type>/<timestamp>/`，最小内容为：

```text
artifacts/<part_type>.SLDPRT
artifacts/<part_type>.STEP
reports/build_report.json
reports/e2e_execution_report.json
release_manifest.json
```

### 自检与验证标准

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

默认 build、test、self-check 和上述字段必须通过。真实通过只能在同次主工作流程产生非空 SLDPRT / STEP、完整报告、QualityGate 通过和发布包后回填。Phase 1 入场时真实 smoke 结果和路径尚待回填；该条件现已由下述 Phase 2 证据关闭。

### 失败和禁止事项

失败必须使用 V1.9 专用证据、法兰、轴、保存、STEP、产物校验或 QualityGate 阶段，详见 `failure_repair.md`。禁止 flange / shaft 自动工程图，禁止并发真实 SolidWorks，禁止用 diagnostic 结果冒充最终验收，禁止进入 V2.0。

## V1.9 Phase 2 执行结果

按 flange → shaft 的串行顺序完成专用 diagnostic 和最终 CLI 主工作流程后，两个 build-only 零件族均已可交付：

| 零件族 | diagnostic | 最终主流程目录 | 最终状态 |
|---|---|---|---|
| `flange_basic` | `CandidatePassed`，100 分审查通过 | `output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/` | `Passed` / `Deliverable` / QualityGate `Passed` |
| `shaft_basic` | `CandidatePassed`，100 分审查通过 | `output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/` | `Passed` / `Deliverable` / QualityGate `Passed` |
| `plate_basic_4holes` | 完整包回归 | `output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260720_082027_397-bd86bc56b48349c69db5f8173c1b3d85/` | `Passed` / `Deliverable` / QualityGate `Passed` |

最终构建报告已经写入 V1.9 diagnostic、视觉审查和主流程通过的 API evidence metadata。先前两次成功 CLI 运行属于 metadata 回填前的过程证据；最终交付必须引用表内目录。

diagnostic 的 body count 与 theoretical volume 仍未自动验证，但专用特征树、四视图、非空产物和完整主工作流程证据已满足本阶段基础族验收。该增强保留为 Improvement，不扩展到 V2.0。

## V2.0-A 通用 CADModelSpec 编译

### 目标与输入输出

输入为 canonical `CADModelSpec`，核心字段包括 `model_id`、`model_type`、`unit`、`parameters`、`reference_geometry`、`sketches`、`features`、`material`、`output_requirements`、`drawing_requirements` 和 `execution_options`。输出为经过图校验的 `SolidWorksBuildPlan`、Validator / Reviewer 结果和 dry-run 产物。

完整规范与 JSON 示例见 `docs/v2_0_a_generic_cad_model_spec.md`。

### 唯一执行链

```text
CADModelSpec
→ CADModelSpecValidator
→ PartTypeRegistry / 零件族 Validator
→ PartFamilyGenericModelFactory
→ SketchDefinition / FeatureDefinition
→ FeatureGraph
→ BuildPlanCompiler
→ SolidWorksBuildPlanValidator
→ SolidWorksBuildPlanReviewer
→ dry-run Worker
```

`FeatureGraph` 是 Schema 到 BuildPlan 和 dry-run 的唯一特征来源。三族定义不得直接创建另一套 operation；`BuildPlanCompiler` 统一生成 `CreateSketch`、构造中心线、十类特征映射、`SavePart` 和 `ExportStep`。

### 草图、特征与三族迁移

`SketchDefinition` 保存草图标识、基准引用、实体、约束、尺寸与可选执行顺序。`FeatureDefinition` 保存特征标识、类型、参数、依赖、草图/特征引用、执行顺序与目标引用。

十类特征是 `extrude_boss`、`extrude_cut`、`revolve_boss`、`revolve_cut`、`fillet`、`chamfer`、`hole`、`linear_pattern`、`circular_pattern`、`mirror`。本阶段这些类型只编译为 BuildPlan operation。

- plate 编译 `plate_base_extrude` → `plate_hole_cut`。
- flange 编译 `flange_body_extrude` → `flange_inner_cut` → `flange_bolt_holes`。
- shaft 编译含构造中心线的 `shaft_profile_sketch` → `shaft_revolve`。

### 验证和失败

缺失特征依赖返回 `feature_dependency_missing`，依赖环返回 `feature_dependency_cycle`，非正、重复或不符合依赖的显式顺序返回 `invalid_feature_order`。草图引用、实体、约束、特征类型和编译失败必须分别使用 `sketch_reference_missing`、`unsupported_sketch_entity`、`unsupported_constraint`、`unsupported_feature_type` 和 `build_plan_compile_failed`。

所有失败都必须在 Worker 前结束。默认 self-check、单元测试与 dry-run 不连接 COM；V2.0 本地交互默认真实策略不适用于这条通用 FeatureGraph 验证链。

### 真实执行边界

V1.9 三族专用真实 Builder 保持可用，仍由 Registry 解析。V2.0-A 不新增通用 Feature Handler，不允许把任意 FeatureGraph 发送给真实 COM，也不得声称任意图已经真实执行。通用真实特征执行延期到 V2.0-B。

禁止装配体、BOM、批量任务队列和大型类型 switch。本阶段完成后停在 V2.0-A。

## V2.0-B 通用 Feature Handler 执行

### 目标与输入输出

V2.0-B 把服务端编译得到的通用 FeatureGraph 计划交给可注册 Handler 边界。输入是带服务端执行来源的 `SolidWorksBuildPlan`；输出是 Registry 解析、参数校验、全图 API evidence 预检和受控执行报告。完整规范见 `docs/v2_0_b_feature_handlers.md`。

### 执行链

```text
canonical CADModelSpec
→ FeatureGraph
→ BuildPlanCompiler
→ 服务端 execution strategy
→ RealSolidWorksWorker 安全检查
→ 全部 operation 适配
→ FeatureHandlerRegistry
→ 全部 Handler 参数校验
→ 全部 Handler API evidence 预检
→ 证据全部 verified 后才连接 COM
→ SolidWorksFeatureGraphPartFamilyBuilder
→ ArtifactValidator
→ Reviewer
→ QualityGate
```

`CreateSketch` / `CreateCenterLine`、`ExtrudeBoss`、`CutExtrude`、`CreateSimpleHole`、`RevolveBoss` 分别适配到 `sketch`、`extrude_boss`、`extrude_cut`、`hole`、`revolve_boss`。Registry 大小写不敏感、拒绝重复注册；未知几何操作返回 `unsupported_feature_type`。`CreateSimpleHole` 是内部 operation 名称，不授权调用 SolidWorks `SimpleHole2`。扩展新 Handler 不修改 Worker，也不增加大型特征类型 switch。

### 来源与兼容边界

Handler 管线来源由服务端根据调用方实际提供的草图和特征写入，JSON 不能自行伪造。通用图使用 `feature_handler_graph`；三族固定生成图继续使用 `part_family_builder` 和 V1.9 专用真实 Builder。因此 V2.0-B 的未验证证据会阻断任意通用图，但不会令 plate、flange、shaft 已验收的固定路径回退。

### 证据门禁

V2.0-B 结束时五类 Handler 的 `api_evidence_status` 全部为 `unverified`。V1.9 的固定零件族结果只属于受限参数轮廓，不等同于通用 API 映射验证。V2.0-C 后续只提升四个精确 profile；整图预检遇到其他未验证轮廓时仍返回 evidence-insufficient，保持 `RealCadConnected=false`、`RealCadExecuted=false` 和 COM 连接计数为零。不得部分执行已通过节点。

### self-check 字段

```text
feature_handler_registry_exists
no_feature_type_large_switch
sketch_handler_registered
extrude_handler_registered
cut_handler_registered
hole_handler_registered
revolve_handler_registered
feature_handler_validation_supported
feature_api_evidence_required
unverified_api_blocks_real_execution
feature_handler_docs_completed
v2_0_b_documented
```

### 验证与禁止事项

验证必须覆盖默认五类注册、大小写查找、重复拒绝、未知类型、非法参数、无需修改 Worker 的扩展、全图 evidence 预检、COM 前阻断及 V1.9 回归。禁止把 `unverified` 提升为真实授权，禁止复制第三方代码，禁止请求伪造执行来源，禁止绕过 Validator、Reviewer、QualityGate，也不扩展装配体、BOM、队列或后续阶段。

## V2.0-C Feature Adapter 执行

### 目标与边界

V2.0-C 保持 Handler 纯逻辑，并通过 `ISolidWorksFeatureAdapter` 隔离真实 COM。`RealSolidWorksFeatureAdapter` 只负责 SolidWorks API 调用，`RealSolidWorksWorker` 负责会话、顺序、结果聚合、保存和导出。完整规范见 `docs/v2_0_c_feature_adapter_execution.md`。

```text
CADModelSpec
→ FeatureGraph / BuildPlan
→ FeatureHandlerRegistry
→ 纯逻辑 Handler 校验与命令
→ ISolidWorksFeatureAdapter
→ RealSolidWorksFeatureAdapter
→ RealSolidWorksWorker 同次结果聚合
→ ArtifactValidator
→ Reviewer
→ QualityGate
```

Handler 禁止引用 SolidWorks Interop、持有 COM 对象或创建会话。新增真实 API 只能进入 Adapter；Worker 不堆叠大型 `switch(feature_type)`。

### 最小候选范围

- 草图只候选 line、rectangle、circle。
- 拉伸只候选 blind extrude。
- 切除只候选 blind `FeatureCut4`。
- 孔使用圆草图加 blind `FeatureCut4`，不得称为或调用 `SimpleHole2` / Hole Wizard。

专用 diagnostic 未真实运行和审查前，状态只能为 `diagnostic_candidate` / `unverified`。未验证特征以 `feature_api_unverified` 阻断生产，不能因 V1.9 专用 Builder 证据而自动放行。

### 诊断与最终验收

专用 diagnostic 输出：

```text
output/solidworks/features/<timestamp>/
├── model.SLDPRT
├── model.STEP
└── feature_execution_report.json
```

专用诊断必须记录每步参数、返回、重建、失败阶段和当次产物。即使诊断通过，也只允许回填接口证据；最终验收必须从命令行主入口 `run-cad-workflow` 进入，并完整经过总工程师编排、工作流引擎、路由、执行器、校验器、复核器、质量门和发布包。

### self-check 与禁止事项

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

self-check、单元测试、CI 和 dry-run 不连接 COM。禁止 Handler COM、未验证生产执行、空产物成功、diagnostic 冒充主流程、绕过质量链或进入 V2.0-D。

### 最终 diagnostic 回填

旧 run `20260730_073759_9143941` 的 Cut/Hole 虽返回非空 Feature 并重建成功，人工复核却发现没有孔，故作废为假成功证据。权威 run 为 `output/solidworks/features/20260730_085830_6592380/`：SolidWorks `33.5.0`、Handler/Adapter `2.0-c.2`、复合源码修订 `feature-execution-source-sha256:71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918`，SLDPRT 73416 bytes、STEP 26403 bytes；7 个特征报告均完成结果、重建和几何变化校验。最终 100/`pass`、`Extrusion` 加两个 `ICE` 与人工双孔确认由同次 E2E 审查报告提供。

Boss、Cut、Hole 的体积依次为 `0→5.9999999999999995E-05`、`5.9999999999999995E-05→5.9214601836602546E-05`、`5.9214601836602546E-05→5.84292036732051E-05` m³。该证据只把 Sketch/Boss/Cut/Hole 的四个精确 profile 提升为 `verified`；`through_all`、`mid_plane`、原生 `SimpleHole2` / Hole Wizard 和任意面仍为 `unverified`。

新 diagnostic 自身仍为 `CandidatePassed`、`NotDeliverable`，`main_workflow_accepted=false` 且 `quality_gate_passed=false`，不得据此提前生成交付结论。其 evidence 审查完成后，独立主流程已从 `run-cad-workflow` 重跑并通过。

### COM 前精确 profile 门禁

`FeatureHandlerRegistry` 在 `ConnectAsync` 前，对每个节点依次执行参数 `Validate` 和 `ValidateEvidenceForRealExecution`：复合源码修订、diagnostic 报告与精确 profile 必须匹配；所有未知参数返回 `invalid_feature_parameter`。Sketch 仅允许 `TopPlane` 且 `constraints` / `dimensions` 为空；Boss 仅允许正深度 blind；Cut 仅允许正深度且 `through_all=false` 的 blind；Hole 仅允许 `simple_circular_cut_blind`、`end_condition=blind`、正直径、正深度且非贯穿，并直接依赖只含一个等直径圆的 `sketch_id`。profile、diagnostic、修订或 Hole 依赖图不匹配返回 `feature_api_unverified`，任一失败均保持 COM 连接计数为零。连接后必须以实际 SolidWorks `33.5.0` 再校验证据版本，不匹配时不得调用建模 API。

### 最终主流程验收

最终 E2E 目录为 `output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260730_090151_162-1b16c3982731423a8ae9a93f1db2dbbe/`。同次 `e2e_execution_report.json` 与 `package_quality_report.json` 记录 `final_status=Passed`、QualityGate `Passed`、`all_source_reports_passed=true` 和 `deliverable_status=Deliverable`。

Feature 报告为 7/7，`all_features_executed`、`all_result_objects_validated`、`all_rebuilds_passed`、`all_geometry_changes_validated`、`artifacts_validated` 均为 `true`，三段体积与 diagnostic 相同。SLDPRT 为 73830 bytes，SHA256 `045A5CF2C445F66B1CE2502065F84805A40CF217600DAD213C68D9EEFBA78612`；STEP 为 26399 bytes，SHA256 `DCAB84553885D804AB961E4BBABB691A7A47C6962D030781211B7B777E1EF7BB`。`review_active_source/feature_pipeline_plate_review_report.json` 审查为 100 分、`pass`，特征树为一个 `Extrusion` 加两个 `ICE`，并人工确认两个孔。四个精确 profile 的完整主流程验收已经关闭，但未扩展到 `through_all`、`mid_plane`、任意面或原生孔 API，且不进入 V2.0-D。

## V2.0-D 参数重建与几何验证执行

V2.0-D 的参数更新只走受控闭环：

~~~text
CADModelSpec
  -> ModelUpdateService
  -> BuildPlanCompiler
  -> FeatureExecutionPipeline
  -> SolidWorks Rebuild
  -> GeometryReader
  -> GeometryValidator
  -> Artifact Validator / Reviewer / QualityGate
~~~

`ModelUpdateService` 必须是纯逻辑层：接收基线 `CADModelSpec` 与参数补丁，计算并写入 `old_parameters`、`new_parameters`、`changed_features`、`rebuild_result`。它必须把受影响的长度、宽度、厚度、孔径或轴径同步映射到既有的 `SketchDefinition` / `FeatureDefinition`，不能只改 `CADModelSpec.Parameters`。初始建模、参数修改和重跑既有 `FeatureGraph` 均走同一条编译与处理器执行路径；不得调用构建器作为捷径，也不得绕过 `FeatureHandlerRegistry` 或 `FeatureHandler`。

GeometryReader 是唯一允许读取 SolidWorks COM 几何的边界，必须把当前受控会话和当前 SLDPRT 的数据转换为 DTO；GeometryValidator 独立、纯逻辑地消费 DTO、输入参数和 FeatureGraph 期望。它必须验证真实 BoundingBox、Body 数量、Volume、可用的 MassProperty、Sketch/Extrude/Cut/Hole 结果，以及 length_mm、diameter_mm 与真实尺寸的一致性。COM 成功返回、文件存在、历史 latest 或 diagnostic 均不是成功证据。

每次重建必须生成同次 rebuild_report.json 与 geometry_validation_report.json，并作为 Artifact Validator、Reviewer、QualityGate 和 ReleasePackage 的源报告；任一报告失败、不可解析或语义不通过均不得交付。最终真实验收只能从：

~~~powershell
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/parameter_update_plate.json
~~~

执行，禁止直接调用 Builder。四孔 plate_basic_4holes 只能复用既有、已注册且已取证的 Feature 类型；不得新增 CAD Feature 或零件族，也不能用模型名、hole_count 或文件存在代替四个真实孔的几何证明。本阶段未通过 QualityGate 前不得进入 V2.0-E。
## V2.0-D 参数更新与三圆 profile

`ModelUpdateService` 只重绑既有 `plate_profile`、`cut_profile`、`hole_profile` 及 `plate_boss`、`plate_cut`、`plate_hole` 的参数，并由 `BuildPlanCompiler` 重建 FeatureGraph。对本轮样例，长度和宽度变化必须把四个孔重映射到 20 mm 边距；厚度变化必须同时重绑 boss 深度与两个盲切深度。它不读取 COM，也不创建 Feature。

真实 Worker 会在 Handler 预检后对编译计划执行 V2.0-D 三圆证据门禁；因此参数更新不得把 Φ10 三圆/单圆 profile 改成未验证的数量、直径、终止条件或位置。任何不匹配应在 COM 前以 `feature_api_unverified` 停止，而不是把 JSON 参数更新误报为成功。

## V2.1-A 夹套零件族执行链

`jacket_basic` 从 `PartTypeRegistry` 读取外径、内径和长度，经 `JacketBasicValidator` 校验后生成 TopPlane 外圆草图、盲拉伸、TopPlane 内圆草图、两倍轴向长度盲切的通用 FeatureGraph，再由 `BuildPlanCompiler` 编译。真实执行只允许通过 `PartFamilyBuilderRegistry` 解析 `JacketFeatureBuilder`，随后进入 `RealSolidWorksWorker`、Artifact Validator、Reviewer、QualityGate 与 ReleasePackage；CLI 不得直接调用 Builder。结构化生产证据未激活时必须在 COM 连接前拒绝，不能以实现存在或示例文件存在视为工作流可用。

当前范围只生成直筒同轴夹套。封头、接管、膨胀节、支座、加强圈、焊缝和装配关系不在 V2.1-A 内。
