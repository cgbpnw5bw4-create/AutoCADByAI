# Extrude Cut Handler API 证据

## 状态与范围

`FeatureType=extrude_cut`，`HandlerVersion=2.0-b.1`，`api_evidence_status=unverified`。本文只覆盖通用拉伸切除；normal cut、薄壁切除和多实体 scope 不在本阶段。

V1.9 plate 与 flange 的专用 Builder 使用固定盲切超深策略。该证据不能证明通用 `through_all` 终止条件或当前 Handler Adapter。

## 参数 Schema

| 参数 | 类型 | 必需 | 校验 |
|---|---|---|---|
| `through_all` | boolean | 否 | 只有解析为 `true` 时满足贯穿语义；当前没有真实映射授权。 |
| `depth_mm` | positive number | 否 | 若使用盲切，必须是有限正数并转换为米。 |

`through_all=true` 与正数 `depth_mm` 至少满足一个，否则返回 `invalid_feature_parameter`。类型不匹配返回 `unsupported_feature_type`。

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
