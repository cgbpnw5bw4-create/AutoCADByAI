# Extrude Cut Handler API 证据

## 状态与范围

`FeatureType=extrude_cut`，`HandlerVersion=2.0-c.2`。正深度、单端、`through_all=false` 的 blind `FeatureCut4` 轮廓已在 V2.0-C 专用诊断中提升为 `api_evidence_status=verified`；normal cut、薄壁切除和多实体 scope 不在本阶段。

V1.9 plate 与 flange 的专用 Builder 使用固定盲切超深策略。该证据不能证明通用 `through_all` 终止条件或当前 Handler Adapter。

## 参数 Schema

| 参数 | 类型 | 必需 | 校验 |
|---|---|---|---|
| `through_all` | boolean | 否 | V2.0-C 精确 profile 必须缺省或为 `false`；`true` 未验证并被拒绝。 |
| `depth_mm` | positive number | V2.0-C 必需 | blind cut 的有限正深度，并转换为米。 |

必须提供正数 `depth_mm` 且 `through_all` 不得为 `true`，否则返回 `invalid_feature_parameter`。类型不匹配返回 `unsupported_feature_type`。

## 官方候选 API

候选为 `IFeatureManager.FeatureCut4`：[SolidWorks 官方 API Help](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureCut4.html)。

候选合同需要确认 `Sd`、`Flip`、`Dir`、`T1`、`T2`、`D1`、`D2`，以及拔模、薄壁、normal cut、feature scope、起始条件和偏移参数。成功应返回 `IFeature`，选择、目标实体或终止条件非法时可能返回 `null`。

## 已有受限证据与缺口

- V1.9 plate/flange 证明活动孔草图上的固定 `FeatureCut4` 专用路径可工作。
- 旧路径以盲切超深近似贯穿，不证明 `through_all=true` 对应的真实枚举和长参数。
- 当前 Handler 的切割草图引用、目标 body 和 scope 没有独立诊断。
- `through_all` 与同时提供的 `depth_mm` 的优先级必须由 Adapter 合同明确，不能临场猜测。

## 提升为 verified 的条件

独立诊断必须分别验证真正的 through-all 与正深度 blind cut，记录证据标识、Handler 版本、源码修订、SolidWorks 版本、精确长参数、单位、活动草图、目标实体、返回 Feature、重建和产物。

负向验证至少包括无切割草图、开放轮廓、无目标实体、无效深度、冲突终止条件和 API 返回 `null`。在同参数轮廓证据经审查前，状态保持 `unverified`，预检返回 `feature_api_evidence_insufficient` 且不连接 COM。

## 禁止事项

禁止把盲切超深描述为已验证 through-all，禁止为绕过证据回退到其他切除 API，禁止忽略 body/scope，禁止复制第三方代码或用历史产物代替诊断。

## V2.0-C Adapter 诊断证据回填

### 状态与参数轮廓

V2.0-C 专用诊断已在 SolidWorks `33.5.0` 中验证 20 mm blind `FeatureCut4`、非空 Feature、成功重建和非空产物。`ExtrudeCutHandler` 保持纯逻辑，不得直接访问 COM。

候选要求切割草图和目标实体精确解析，深度由毫米转换为米，终止条件固定为 blind，并记录 `FeatureCut4` 完整参数、返回 Feature、重建和切除结果。`through_all`、normal cut、thin、多实体 scope 均不在 V2.0-C 候选中；不得用超深盲切冒充已验证贯穿。

### diagnostic 与回填

专用 diagnostic 输出 `output/solidworks/features/<timestamp>/model.SLDPRT`、`model.STEP`、`feature_execution_report.json`。报告必须区分调用失败与返回无效 Feature，并绑定当次草图、目标、深度、API 参数和产物。

诊断和审查前以 `feature_api_unverified` 阻断生产；诊断成功只允许 evidence 回填。最终验收仍从 `run-cad-workflow` 经过 Validator、Reviewer 和 QualityGate。

旧 run `20260730_073759_9143941` 的 Cut 虽返回非空 Feature 且重建通过，人工复核却没有孔，因此是 `feature_result_invalid` 反例，不得保留为 verified。

现行证据标识为 `v2.0-e-20260821-034143-extrude-cut`，报告为 `evidence/solidworks/20260821_034143_9836278/feature_execution_report.json`。三圆 blind `FeatureCut4` 使 200×100×15 mm 模型体积从 `0.00030000000000000003` 降至 `0.00029646570826471146 m³`。证据绑定 SolidWorks `31.5.0` 与完整执行链源码 revision `feature-execution-source-sha256:1795e60b5855ee1140db9b979d34ae0672490d4f7e384ec820c8acadc9baa211`。

只授权 `blind;single_end;positive_depth_mm;through_all_false;no_thin;single_body_scope`。`through_all`、normal cut、thin 和多实体 scope 仍为 `unverified`。新 diagnostic 仍为 `NotDeliverable`，最终验收待 `run-cad-workflow`。禁止 Handler COM、扩大参数轮廓和无效 Feature 成功。
