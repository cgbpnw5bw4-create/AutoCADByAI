# Hole Handler API 证据

## 状态与范围

`FeatureType=hole`，`HandlerVersion=2.0-c.2`。圆草图直径匹配加正深度 blind `FeatureCut4` 的组合轮廓已在 V2.0-C 专用诊断中提升为 `api_evidence_status=verified`；这不是 Hole Wizard 或 `SimpleHole2` 授权。

历史 BuildPlan operation 名称 `AddHoleWizardHole` 只是内部语义标签，不是 API 成功证据；V2.0-C 当前 operation 为 `CreateSimpleHole`，同样不授权 SolidWorks `SimpleHole2`。V1.9 明确使用草图圆加 `FeatureCut4`，没有验证通用 Hole Wizard Handler。

## 参数 Schema

| 参数 | 类型 | 必需 | 校验 |
|---|---|---|---|
| `hole_diameter_mm` | positive number | 条件必需 | 与兼容别名至少提供一个有限正数。 |
| `diameter_mm` | positive number | 条件必需 | `hole_diameter_mm` 缺失时作为兼容别名。 |
| `depth_mm` | positive number | V2.0-C 必需 | blind `FeatureCut4` 的有限正深度。 |
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

## V2.0-C 圆草图加 blind cut 证据回填

### 选定策略与状态

V2.0-C 选定唯一 diagnostic 候选，2026-07-30 新 run 已把其精确 profile 提升为 `verified`：

```text
孔位置与正直径
→ RealSolidWorksFeatureAdapter 创建独立圆草图
→ 精确保持或恢复该切割草图选择
→ 正深度 blind FeatureCut4
→ 校验返回 Feature、重建和孔几何
```

这是组合策略，不是 `SimpleHole2`，也不是 Hole Wizard。内部 operation `CreateSimpleHole` 不得被解释为同名或近名真实 API 选择；历史 `AddHoleWizardHole` 已退出当前适配映射。专用诊断已验证直径 10 mm 圆草图和深度 20 mm blind cut 的精确轮廓。

### API 与参数轮廓

圆草图使用 [`ISketchManager.CreateCircle`](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateCircle.html)，切除使用 [`IFeatureManager.FeatureCut4`](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureCut4.html)。参数必须包含受控基准/位置、正直径、毫米到米转换和明确正数 blind 深度。

`HoleHandler` 只校验并生成组合命令，不得引用 COM。`SimpleHole2`、`HoleWizard5`、孔标准/类型枚举、贯穿猜测和不可审计的超深默认值均被拒绝。

### diagnostic 与回填

专用 diagnostic 必须把圆草图子步骤和 `FeatureCut4` 子步骤分别记录到 `output/solidworks/features/<timestamp>/feature_execution_report.json`，并同时生成非空 `model.SLDPRT` 和 `model.STEP`。任一子步骤失败使用 `hole_execution_failed`，返回或重建无效使用 `feature_result_invalid`。

旧 run `20260730_073759_9143941` 的 Hole 虽返回非空 Feature 且重建通过，人工复核却没有孔，因此是明确假成功，不得保留为 verified。

现行证据标识为 `v2.0-e-20260821-034143-simple-hole`，报告为 `evidence/solidworks/20260821_034143_9836278/feature_execution_report.json`。直径 10 mm 圆草图加 blind `FeatureCut4` 使 200×100×15 mm 模型体积从 `0.0002964657082647115` 降至 `0.00029528761101961536 m³`；证据绑定 SolidWorks `31.5.0` 与完整执行链源码 revision `feature-execution-source-sha256:1795e60b5855ee1140db9b979d34ae0672490d4f7e384ec820c8acadc9baa211`。Registry 在 `ConnectAsync` 前验证依赖草图恰为一个等径圆。

只授权 `simple_circular_cut_blind;diameter_matches_single_circle;positive_depth_mm;no_wizard`。原生 `SimpleHole2`、Hole Wizard、其他终止条件和任意放置引用仍为 `unverified`。新 diagnostic 仍为 `CandidatePassed` / `NotDeliverable`；最终验收必须从 `run-cad-workflow` 进入完整主流程。禁止更换 API、Handler COM、扩大参数轮廓或进入 V2.0-D。
