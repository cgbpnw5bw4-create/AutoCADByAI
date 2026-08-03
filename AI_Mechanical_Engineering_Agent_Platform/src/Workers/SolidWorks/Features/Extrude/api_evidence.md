# Extrude Boss Handler API 证据

## 状态与范围

`FeatureType=extrude_boss`，`HandlerVersion=2.0-c.2`。正深度、单端、无拔模、无薄壁的 blind 轮廓已在 V2.0-C 专用诊断中提升为 `api_evidence_status=verified`；其他拉伸变体不在本阶段。

V1.9 plate 与 flange 的专用 Builder 已使用固定盲拉伸参数，但没有验证当前 Handler 对通用 `direction`、深度、草图引用和长参数列表的映射。

## 参数 Schema

| 参数 | 类型 | 必需 | 校验 |
|---|---|---|---|
| `depth_mm` | positive number | 是 | 必须是有限正数，并在真实 Adapter 中转换为米。 |
| `direction` | `blind` | 否 | V2.0-C 精确 profile 只允许 blind；`mid_plane` 返回 `invalid_feature_parameter`。 |

Handler 类型不匹配返回 `unsupported_feature_type`。参数合法只表示可以生成 BuildPlan，不表示可以执行真实 API。

## 官方候选 API

候选为 `IFeatureManager.FeatureExtrusion2`：[SolidWorks 官方 API Help](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureExtrusion2.html)。

候选合同要求明确 `Sd`、`Flip`、`Dir`、`T1`、`T2`、`D1`、`D2`，以及拔模、薄壁、合并、feature scope、auto-select、起始条件和偏移参数。成功应返回 `IFeature`，失败可能返回 `null`。

真实调用前必须有有效闭合草图处于活动或已选状态，毫米深度必须转换为米，终止条件必须与本次证据参数轮廓完全一致。

## 已有受限证据与缺口

- V1.9 plate/flange 证明固定专用 Builder 的盲拉伸路径可工作。
- 该结果没有绑定 `ExtrudeBossHandler`、当前 BuildPlan Adapter 或通用引用解析。
- `direction=mid_plane` 不能从盲拉伸结果推导。
- 草图选择错误、参数数量或终止条件错误都可能返回 `null`。
- 薄壁、拔模和多实体 scope 没有本阶段证据。

## 提升为 verified 的条件

独立 Handler 诊断必须记录证据标识、版本、源码修订、SolidWorks 版本、精确长参数、单位转换、草图选择状态、返回 Feature、重建结果和产物路径。至少分别验证 `blind` 与 `mid_plane`，并对缺失草图、零/负深度、无效枚举和 API 返回 `null` 提供负向结果。

证据必须绑定当前 Handler 版本和准确 `ParameterProfile`。在参数轮廓逐项审查前，状态保持 `unverified`，全图预检以 `feature_api_evidence_insufficient` 在 COM 前阻断。

## 禁止事项

禁止复用专用 Builder 成功结果直接授权通用映射，禁止猜测 `FeatureExtrusion2` 长参数，禁止把 `mid_plane` 静默降级为 `blind`，禁止复制第三方代码。

## V2.0-C Adapter 诊断证据回填

### 状态与参数轮廓

V2.0-C 专用诊断已在 SolidWorks `33.5.0` 中验证 10 mm blind `FeatureExtrusion2`、非空 Feature、成功重建和非空产物。`ExtrudeBossHandler` 仍只能校验 `depth_mm` 和生成命令，严禁直接调用 COM。

候选要求闭合草图已由依赖结果精确解析，深度由毫米转换为米，end condition 固定为 blind，并记录完整长参数、返回 Feature 和重建。`mid_plane`、thin、draft、offset 和复杂 feature scope 不属于 V2.0-C 候选。

### diagnostic 与回填

专用 diagnostic 必须输出当次 `model.SLDPRT`、`model.STEP` 和 `feature_execution_report.json` 到 `output/solidworks/features/<timestamp>/`，并记录草图引用、深度、全部 API 参数、返回对象、重建和实体结果。

未运行或未审查时以 `feature_api_unverified` 阻断生产；返回空 Feature 或失败重建使用 `feature_result_invalid`。diagnostic 通过只支持 evidence 审查，最终验收仍只能从 `run-cad-workflow` 进入完整链路。

权威证据标识为 `v2.0-c-20260730-085830-extrude-boss`，报告为 `output/solidworks/features/20260730_085830_6592380/feature_execution_report.json`。10 mm blind boss 的体积从 `0` 增至 `6E-05` m³，特征树包含 `Extrusion`；证据绑定完整执行链源码 revision `71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918`。

只授权 `blind;single_end;positive_depth_mm;no_draft;no_thin;merge_result`。`mid_plane`、其他 end condition、draft、thin 和复杂 scope 仍为 `unverified`。本 diagnostic 仍为 `NotDeliverable`，最终验收待 `run-cad-workflow`。禁止 Handler COM、扩大参数轮廓、接受空 Feature 或进入 V2.0-D。
