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

真实 `SolidWorks` 执行默认关闭。只有请求中 `allow_real_cad_execution=true`、`dry_run=false`，并且环境变量 `SW_ENABLE_REAL_EXECUTION=true` 时，才允许进入真实连接路径。

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

默认 self-check 不执行真实工程图。真实工程图 smoke test 必须同时设置 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_SMOKE_TEST=true`。

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

默认 self-check 不执行真实尺寸标注。真实尺寸 smoke test 必须同时设置 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_DIMENSION_SMOKE_TEST=true`。严格模式使用 `SW_STRICT_REAL_DRAWING_DIMENSION_TEST=true`。

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

默认 self-check 不执行真实标题栏测试。真实标题栏 smoke test 必须同时设置 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_TITLE_BLOCK_SMOKE_TEST=true`。严格模式使用 `SW_STRICT_REAL_DRAWING_TITLE_BLOCK_TEST=true`。

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

默认主流程仍走 `FakeSolidWorksWorker`，不会启动 SolidWorks。只有请求上下文同时声明 `allow_real_cad_execution=true` 和 `dry_run=false`，并且环境变量 `SW_ENABLE_REAL_EXECUTION=true` 时，`SolidWorksMainWorkflowRunner` 才允许选择 `RealSolidWorksWorker`。`Agent`、`Gateway` 和 `LLM` 仍不能直接调用 Worker。

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

四阶段源文件必须使用同一 request 的精确绝对路径传递，不得从历史 latest 目录拼接。最终包写入 `output/solidworks/e2e/plate_basic_4holes/<timestamp>/`；阶段产物保持在既有 `output/solidworks/real/` 受信任根，以满足原有 Validator。明确请求真实执行但缺少任一四重确认时必须失败关闭，不能使用 Fake Worker 作为验收结果。

## V1.7-REAL-AUTH 本地开发授权

本地开发人员可在不提交的 `config/solidworks.local.json` 中显式启用 `LocalDevelopmentProfile`。CLI 读取有效配置后自动形成 `allow_real_cad_execution=true` 与 `dry_run=false` 的内部请求，并在平台创建前设置真实执行环境；结构化业务输入只描述零件和交付内容，不重复承担授权字段。

该便利不改变产品边界：配置不存在或无效时真实 CAD 仍被禁止；Gateway、Agent 和 LLM 仍不能直接访问 Worker；完整流程仍必须穿过 WorkflowEngine、ArtifactValidator、Reviewer、QualityGate 与 ReleasePackage。默认 self-check 不读取或应用本地授权配置，因此不会启动 SolidWorks。

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

真实执行必须通过请求层、`LocalDevelopmentProfile` 本地授权层和环境层。默认 self-check 不连接 COM。真实任务全局串行，阶段验收顺序为 `flange_basic` → `shaft_basic`。

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
