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

## 成功标准

默认 self-check 必须 Passed，且不能启动真实 CAD。涉及真实 API 的修复必须先在诊断 Runner 中验证，再回填 Worker。
