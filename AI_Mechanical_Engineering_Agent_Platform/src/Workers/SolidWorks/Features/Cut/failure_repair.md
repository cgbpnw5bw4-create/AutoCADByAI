# Extrude Cut Handler 失败修复

## 目标与输入输出

本手册处理 `extrude_cut` 的类型、终止条件、深度和 API evidence 失败。输入为 Feature 参数、切割草图/目标引用、validation issues 与证据状态；输出为明确失败阶段和无 COM 关闭条件。

## 失败映射

| `failure_stage` | 直接原因 | 修复动作 | 关闭条件 |
|---|---|---|---|
| `unsupported_feature_type` | 类型不是 `extrude_cut` 或未注册 | 修正类型或补独立 Handler 注册 | Registry 可解析，未知类型继续拒绝 |
| `invalid_feature_parameter` | 既没有 `through_all=true`，也没有正数 `depth_mm` | 明确选择贯穿或盲切深度；不得用超深默认值掩盖语义 | 参数校验通过，非法样例不连接 COM |
| `feature_api_evidence_insufficient` | `FeatureCut4` 的通用终止映射为 `unverified` | 分别诊断真实 through-all 和 blind 参数轮廓 | 当前 Handler 版本、目标 scope 和返回合同均被验证 |

## 修复步骤

1. 先校验 `through_all` 布尔语义和有限正深度。
2. 检查上游切割草图、目标实体和依赖，不在 Handler 中猜目标 body。
3. 执行整图适配、参数校验和 evidence 预检。
4. 精确 blind profile 可使用 V2.0-C 已验证 evidence；其他 profile 必须在 COM 前以 evidence-insufficient 停止。
5. 独立诊断必须记录 `FeatureCut4` 完整参数、活动草图、scope、返回 Feature、重建和几何结果。

## 禁止事项

禁止把 V1.9 盲切超深称为已验证 through-all，禁止静默改变终止条件，禁止忽略多实体 scope，禁止复制第三方代码。

## V2.0-C Adapter 失败修复

| `failure_stage` | 直接原因 | 修复与关闭条件 |
|---|---|---|
| `feature_adapter_missing` | Worker 无可用真实 Adapter | 修复接口注入；`ExtrudeCutHandler` 仍保持纯逻辑。 |
| `cut_execution_failed` | 切割草图、目标、blind 深度、调用或重建失败 | 在专用 diagnostic 中逐项记录并证明切除结果。 |
| `feature_result_invalid` | `FeatureCut4` 返回空/无效 Feature 或重建失败 | 阻断后续步骤，补结果、依赖和重建校验。 |
| `feature_artifact_missing` | SLDPRT、STEP 或报告不属于当次运行 | 修复当次保存/导出，禁止历史 latest。 |
| `feature_api_unverified` | 请求超出已验证 `through_all=false`、正深度 blind、单实体 scope profile | 停止生产；不得扩大到 through-all、normal cut、thin 或多实体。 |

旧 run `20260730_073759_9143941` 的非空 Cut 被人工判定为无孔假成功，必须撤销。权威 run `20260730_085830_6592380` 以体积下降、第一个 `ICE` 和视觉孔确认关闭该问题。修复不得切换为 `through_all`、normal cut、thin 或多实体 scope；diagnostic 仍为 `NotDeliverable`，最终从 `run-cad-workflow` 验收。
