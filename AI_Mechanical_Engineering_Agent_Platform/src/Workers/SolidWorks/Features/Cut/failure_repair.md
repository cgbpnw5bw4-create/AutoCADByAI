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
4. 当前状态必须在 COM 前以 `feature_api_evidence_insufficient` 停止。
5. 独立诊断必须记录 `FeatureCut4` 完整参数、活动草图、scope、返回 Feature、重建和几何结果。

## 禁止事项

禁止把 V1.9 盲切超深称为已验证 through-all，禁止静默改变终止条件，禁止忽略多实体 scope，禁止复制第三方代码。
