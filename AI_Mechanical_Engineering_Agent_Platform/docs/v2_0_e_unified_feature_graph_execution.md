# V2.0-E：统一 FeatureGraph 真实执行与证据闭环

## 目标

V2.0-E 将已注册零件族的真实建模入口统一到 `SolidWorksFeatureGraphPartFamilyBuilder`。执行链固定为：

`CADModelSpec → BuildPlanCompiler → FeatureHandlerRegistry → RealSolidWorksFeatureAdapter → ForceRebuild3 → STEP 内容校验 → ArtifactValidator → Reviewer → QualityGate`

零件族不得在 Worker 中绕过该链直接调用 COM，也不得把候选诊断当作发布包成功。

## 已授权范围

- `plate_basic_4holes` 的 160×80×12 → 200×100×15 参数更新链已采集真实候选证据。
- 候选证据：`evidence/solidworks/20260821_034143_9836278/feature_execution_report.json`。
- 该证据记录 SolidWorks `31.5.0`、真实 COM 执行、三圆盲切与第四孔的体积递减、非空 SLDPRT，以及以 `ISO-10303-21;` 开始且含 `ENDSEC;` / `END-ISO-10303-21;` 的 STEP。
- 该报告的状态必须保持 `CandidatePassed` 与 `NotDeliverable`。最终可交付结论仍只能由 `run-cad-workflow` 的同次 Workflow、Validator、Reviewer 和 QualityGate 产生。

## fail-closed 规则

- STEP 扩展名、非空文件或 COM 返回成功均不足以通过；物理文件必须通过 `CadArtifactContentValidator.TryValidateStepFile`。
- Handler 证据、诊断路径、诊断内物理工件、SolidWorks 版本和源码 revision 任一失配，必须在 COM 连接前以 `feature_api_unverified` 拒绝。
- V2.0-D 的参数更新候选必须真实应用 `parameter_update`，并按更新后的 200×100×15 几何和体积转换验证；不得拿 160×80×12 的初始体积冒充更新证据。
- `revolve_boss` 仍为 `Unverified`，`shaft_basic` 必须失败关闭。

## V2.1-A 冻结边界

`jacket_basic` 的 schema、BuildPlan 与 dry-run 可以保留，但 `SupportsRealExecution=false`。V2.0-E 的四类 Feature 证据不得自动授权夹套真实执行；在夹套自己的结构化证据、实测几何和同次主流程发布包恢复前，它必须在连接 COM 前停止。

## 验收

`platform_self_check_report.json` 中以下字段共同构成 V2.0-E 阶段结论：

- `v2_0_e_unified_part_family_builders`
- `v2_0_e_controlled_plate_evidence_active`
- `v2_0_e_step_content_gate_active`
- `v2_1_a_real_execution_frozen`
- `v2_0_e_documented`
- `v2_0_e_final_status`

全平台 `final_status` 仍会包含 V2.1-A 的冻结状态；它为 `Failed` 时不得被解释为 V2.0-E 已失败，也不得被改写为全平台通过。
