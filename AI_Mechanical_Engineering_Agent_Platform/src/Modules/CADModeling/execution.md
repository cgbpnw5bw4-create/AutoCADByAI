# CADModeling 执行说明

## 目标

`CADModeling` 模块负责把结构化需求转换为可执行的 CAD 建模计划，并通过受控 Worker 生成或验证产物。当前 `SolidWorks` 能力仍以受控 `plate_basic_4holes` 场景和 dry-run / smoke test 为主。

## 标准链路

```text
CADModelSpec
→ SolidWorksBuildPlanSkill
→ SolidWorksBuildPlanValidator
→ SolidWorksWorkerRequest
→ FakeSolidWorksWorker 或 RealSolidWorksWorker
→ SolidWorksMainWorkflowRunner 在主流程中串接 Worker、Validator、Reviewer 和 QualityGate
→ 可选 V1.1 SolidWorksDrawingBuilder 生成基础工程图
→ 可选 V1.2 SolidWorksDrawingDimensionBuilder 生成基础尺寸工程图
→ 可选 V1.3 SolidWorksDrawingTitleBlockBuilder 写入标题栏基础信息
→ 可选 V1.4 SolidWorksReleasePackageBuilder 收集发布包
→ SolidWorksArtifactValidator
→ SolidWorksBuildPlanReviewer
→ QualityGate
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

CAD 主流程只接受显式 `solidworks_main_workflow`、结构化 `cad_model_type=plate_basic_4holes`，或明确包含 `plate_basic_4holes` 的受控零件请求。仅泛化提到 `SolidWorks` 不构成触发条件。无法识别或不受支持的 `CADModelSpec` 必须保持在普通内部协作路径，不得静默改写为 `plate_basic_4holes` 后启动真实 CAD。

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
