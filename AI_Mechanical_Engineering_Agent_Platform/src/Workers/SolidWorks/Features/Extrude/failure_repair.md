# Extrude Boss Handler 失败修复

## 目标与输入输出

本手册处理 `extrude_boss` 的类型、深度、方向和 API evidence 失败。输入为 Feature 参数、活动草图引用、validation issues 和证据状态；输出为可行动失败阶段、无 COM 回归结果或独立诊断要求。

## 失败映射

| `failure_stage` | 直接原因 | 修复动作 | 关闭条件 |
|---|---|---|---|
| `unsupported_feature_type` | 类型不是 `extrude_boss` 或 Registry 缺失 | 修正类型或补完整独立注册 | Registry 解析成功且 Worker 无新增类型 switch |
| `invalid_feature_parameter` | `depth_mm` 缺失、非有限正数，或 `direction` 不属于声明枚举 | 修正参数；不得截断、取绝对值或静默改方向 | 纯校验通过，非法样例在 COM 前拒绝 |
| `feature_api_evidence_insufficient` | `FeatureExtrusion2` 通用映射仍为 `unverified` | 停止执行；完成 blind / mid-plane 独立诊断和长参数审查 | 精确参数轮廓绑定当前 Handler 版本并被审查为 `verified` |

## 修复步骤

1. 先确认正数毫米深度和 `blind|mid_plane` 枚举。
2. 检查上游草图引用与闭合性；不能在 Extrude Handler 内重建未知草图。
3. 重跑 Registry、参数校验和整图 evidence 预检。
4. 精确 blind profile 已有 V2.0-C evidence；其他拉伸轮廓未验证时仍确认 `RealCadConnected=false`、`RealCadExecuted=false`。
5. API 修复只在独立诊断中记录完整 `FeatureExtrusion2` 参数、单位、返回 Feature、重建和产物。

## 禁止事项

禁止把 `mid_plane` 降级为 `blind`，禁止猜测 COM 长参数，禁止用 V1.9 plate/flange 专用成功替代 Handler 诊断，禁止复制第三方代码。

## V2.0-C Adapter 失败修复

| `failure_stage` | 直接原因 | 修复与关闭条件 |
|---|---|---|
| `feature_adapter_missing` | Worker 无可用 Adapter | 修复 `ISolidWorksFeatureAdapter` 注入，不在 Handler 内实例化。 |
| `extrude_execution_failed` | 草图解析、blind 参数、调用或重建失败 | 在专用 diagnostic 中记录完整 `FeatureExtrusion2` 参数并证明实体结果。 |
| `feature_result_invalid` | 返回空/无效 Feature、依赖或重建失败 | 阻断后续步骤，补返回和重建检查。 |
| `feature_artifact_missing` | 当次 SLDPRT、STEP 或报告缺失/为空 | 修复当次保存/导出，禁止读取历史 latest。 |
| `feature_api_unverified` | 请求不符合已验证单端正深度 blind、无 draft/thin、合并结果 profile | 停止生产；`mid_plane` 和其他 end condition 不得借用 blind evidence。 |

权威 evidence 为 `20260730_085830_6592380` 的体积 `0→6E-05` m³。只修正精确 blind 轮廓，不把 `mid_plane`、thin、draft 或复杂 scope 纳入本阶段。diagnostic 仍为 `NotDeliverable`，必须从 `run-cad-workflow` 最终验收；禁止 Handler COM、假成功和进入 V2.0-D。
