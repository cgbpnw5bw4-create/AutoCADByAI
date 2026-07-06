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
→ 可选 V1.1 SolidWorksDrawingBuilder 生成基础工程图
→ 可选 V1.2 SolidWorksDrawingDimensionBuilder 生成基础尺寸工程图
→ 可选 V1.3 SolidWorksDrawingTitleBlockBuilder 写入标题栏基础信息
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
