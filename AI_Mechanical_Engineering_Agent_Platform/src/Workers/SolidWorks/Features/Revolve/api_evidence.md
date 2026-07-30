# Revolve Boss Handler API 证据

## 状态与范围

`FeatureType=revolve_boss`，`HandlerVersion=2.0-b.1`，`api_evidence_status=unverified`。本文只覆盖实体基体旋转；开放轮廓、薄壁旋转、旋转切除和未验证的部分角度参数轮廓不在本阶段授权范围。

V1.9 shaft diagnostic 通过了固定闭合半轮廓、中心线、selection mark `16` 和 360 度的专用 20 参数序列，但没有绑定通用引用 Adapter。

## 参数 Schema

| 参数 | 类型 | 必需 | 校验 |
|---|---|---|---|
| `angle_degrees` | number `(0,360]` | 是 | 必须有限且大于 0、不超过 360；真实调用时转换为弧度。 |
| `profile_selection_mark` | integer | 否 | 候选值为 `0`，尚待 Handler 诊断验证。 |
| `axis_selection_mark` | integer | 否 | 候选值为 `16`，尚待 Handler 诊断验证。 |

角度非法返回 `invalid_feature_parameter`；类型不匹配返回 `unsupported_feature_type`。

## 官方候选 API

| API | 官方资料 | 候选用途 |
|---|---|---|
| `IFeatureManager.FeatureRevolve2` | [SolidWorks 官方 API Help](https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureRevolve2.html) | 旋转实体；必须固定全部 20 参数语义和返回 `IFeature` 合同。 |
| `ISelectData.Mark` | [SolidWorks 官方 API Help](https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISelectData~Mark.html) | 候选 profile mark `0`、axis mark `16` 的选择数据。 |
| `IEntity.Select4` | [SolidWorks 官方 API Help](https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IEntity~Select4.html?format=P&value=) | 使用带 Mark 的选择数据选取轮廓与旋转轴。 |

成功候选应返回 `IFeature`，失败可能返回 `null`。调用前必须解析闭合半轮廓和构造中心线，并保持准确选择顺序。

## 已有受限证据与缺口

- V1.9 shaft 专用诊断证明一条固定 360 度路径。
- 它没有证明任意草图结果引用可解析，也没有绑定当前 Handler 或 FeatureGraph Adapter。
- 部分角度、双向、反向、合并和 scope 的参数语义尚未逐项诊断。
- 非空 Feature 仍需结合重建和几何审查。

## 提升为 verified 的条件

独立 Handler 诊断必须记录证据标识、版本、源码修订、SolidWorks 版本、草图/轴引用、selection mark、选择顺序、角度单位、完整参数、返回 Feature、重建状态、几何审查和产物路径。

至少验证 360 度和一个受支持的部分角度轮廓，并覆盖无轴、开放轮廓、错误 mark、角度越界、引用解析失败和 API 返回 `null`。若本阶段不支持部分角度，Adapter 必须在参数校验中明确拒绝，而不能以未验证调用尝试执行。

在准确参数轮廓经审查前，状态保持 `unverified`，全图预检返回 `feature_api_evidence_insufficient`，COM 连接计数为零。

## 禁止事项

禁止把 shaft 专用 360 度证据泛化为任意旋转，禁止猜测 selection mark 或长参数，禁止把偏移拉伸作为静默回退，禁止复制第三方代码。
