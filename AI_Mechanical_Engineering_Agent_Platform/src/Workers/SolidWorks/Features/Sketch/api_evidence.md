# Sketch Handler API 证据

## 状态与范围

`FeatureType=sketch`，`HandlerVersion=2.0-b.1`，`api_evidence_status=unverified`。本文只记录通用草图 Handler 的候选 API、受限历史证据和验证缺口，不授权真实 COM 执行。

V1.9 零件族诊断只覆盖固定基准上的部分直线、圆和矩形组合；它没有验证通用引用解析、全部草图实体、约束、尺寸或轮廓闭合性，因此不能提升本 Handler 的状态。

## 参数 Schema

| 参数 | 类型 | 必需 | 校验 |
|---|---|---|---|
| `reference_plane` | reference | 是 | 必须为非空的精确基准面或平面引用，否则返回 `sketch_reference_missing`。 |
| `entities` | `SketchEntity[]` JSON | 是 | 必须是合法非空数组，且实体类型属于声明集合。 |
| `constraints` | `SketchConstraint[]` JSON | 否 | 当前只保留结构化输入，不构成真实约束执行证据。 |
| `dimensions` | dictionary JSON | 否 | 当前只保留结构化输入，不构成真实尺寸驱动证据。 |

实体 JSON 非法返回 `invalid_feature_parameter`；数组为空或含未支持实体返回 `unsupported_sketch_entity`。

## 官方候选 API

| API | 官方资料 | 当前用途与证据边界 |
|---|---|---|
| `ISketchManager.InsertSketch` | [SolidWorks 官方 API Help](https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~InsertSketch.html) | 候选草图进入/退出调用；其本身没有证明特征成功的返回合同。 |
| `ISketchManager.CreateLine` | [SolidWorks 官方 API Help](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateLine.html) | 候选直线创建；返回非空实体不等于闭合轮廓有效。 |
| `ISketchManager.CreateCircle` | [SolidWorks 官方 API Help](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateCircle.html) | 候选圆创建；坐标必须由毫米转换为米。 |
| `ISketchManager.CreateCenterRectangle` | [SolidWorks 官方方法索引](https://help.solidworks.com/2026/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.isketchmanager_methods.html) | 候选中心矩形；尚无绑定当前 Adapter 的独立诊断。 |

真实执行前还必须证明目标基准或面已经精确选择。`TopFace` 和任意面引用当前未实现为通用解析合同。

## 已有受限证据

- V1.9 plate、flange、shaft 的专用 Builder/diagnostic 使用过上述 API 的部分子集。
- 这些证据绑定固定零件族、固定选择顺序和固定参数，不绑定当前 `SketchHandler`。
- 非空草图实体、专用 Builder 成功或历史产物存在都不能证明通用草图有效。

## 提升为 verified 的条件

独立诊断必须记录 `EvidenceId`、`HandlerVersion`、`ParameterProfile`、SolidWorks 版本、`DiagnosticRunPath`、`SourceRevision`，并至少验证：

1. 服务端 reference 到前/上/右基准面及受控平面引用的解析和选择。
2. 当前声明实体的坐标单位、API 返回对象、草图进入/退出和重建结果。
3. 非空但开放、重叠或无效轮廓的负向场景。
4. `constraints` 与 `dimensions` 要么获得逐类证据，要么在执行 Adapter 中明确拒绝。
5. 诊断生成的模型由后续特征消费，并通过非空产物和几何审查。

全部结果可重复且经审查前，状态保持 `unverified`，预检返回 `feature_api_evidence_insufficient`，不得连接 COM。

## 禁止事项

禁止把 V1.9 专用草图路径视为通用授权，禁止用实体非空代替轮廓有效性，禁止猜测任意面引用，禁止复制第三方代码或接入第三方脚本。
