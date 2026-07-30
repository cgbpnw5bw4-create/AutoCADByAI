# Hole Handler API 证据

## 状态与范围

`FeatureType=hole`，`HandlerVersion=2.0-b.1`，`api_evidence_status=unverified`。当前没有被项目接受的通用 Hole API 映射，也没有授权的返回合同。

BuildPlan operation 名称 `AddHoleWizardHole` 只是内部语义标签，不是 API 成功证据。V1.9 明确使用草图圆加 `FeatureCut4`，没有验证通用 Hole Wizard Handler。

## 参数 Schema

| 参数 | 类型 | 必需 | 校验 |
|---|---|---|---|
| `hole_diameter_mm` | positive number | 条件必需 | 与兼容别名至少提供一个有限正数。 |
| `diameter_mm` | positive number | 条件必需 | `hole_diameter_mm` 缺失时作为兼容别名。 |
| `end_condition` | text | 否 | 当前没有已授权的真实映射。 |

没有有效正直径时返回 `invalid_feature_parameter`。即使参数合法，孔位置、方向、终止、标准和类型仍未解析，不允许真实执行。

## 被拒绝的官方研究候选

`IFeatureManager.HoleWizard5` 官方资料见 [SolidWorks 官方 API Help](https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeatureManager~HoleWizard5.html)。

该 API 目前仅作为研究候选，不能用作授权，原因包括：

- 标准、孔类型和尺寸标识尚无稳定映射。
- 放置引用、方向和终止语义尚未解析。
- 当前 Adapter 没有独立诊断和可审计返回合同。
- V1.9 已拒绝盲用 Hole Wizard，并选择证据更充分的草图加切除专用途径。

项目也没有接受另一种“简单孔 API”作为通用生产策略。若未来选择草图加切除组合，应明确它是组合 Handler 还是 Hole Handler 内部策略，并补完整的事务、失败阶段和证据。

## 提升为 verified 的条件

必须先作策略评审，再为唯一选定策略建立独立诊断。证据至少记录 `EvidenceId`、`HandlerVersion`、准确参数轮廓、SolidWorks 版本、源码修订、位置/方向/终止引用、标准与类型标识、完整 API 参数、返回对象、重建结果、孔几何审查和产物路径。

负向验证必须覆盖直径非法、无放置参考、方向歧义、终止条件不支持、标准/类型无效和 API 失败。所有信息经审查前状态保持 `unverified`，真实预检返回 `feature_api_evidence_insufficient` 并在 COM 前停止。

## 禁止事项

禁止因 operation 名称含 Hole Wizard 就调用 `HoleWizard5`，禁止复用 V1.9 草图切除结果宣称 Hole Handler 已验证，禁止猜测标准或终止枚举，禁止复制第三方代码。
