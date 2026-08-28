# V2.1-A 复杂特征库

## 目标

在不改变现有架构的前提下扩展 Feature Library，为常用机械设计特征建立 Handler、参数契约、Adapter 执行入口与证据文档。

本轮最终交付：五个特征全部完成 Handler、参数契约、Adapter 真实实现与文档，并在补齐实体引用模型后**全部完成真机取证**，五个 API 状态均为 `verified`。

## 版本号说明

`V2.1-A` 这个标签此前曾指向圆筒夹套零件族。经用户决定，**该用法作废**，`V2.1-A` 现指本轮的复杂特征库。夹套零件族本身继续有效，只是不再占用 `V2.1-A` 编号，其文档 `docs/v2_1_a_jacket_basic.md` 与自检字段 `v2_1_a_jacket_documented` 保留，仅作历史记录，不再代表阶段编号。

## 适用范围

扩展 Feature Library、为复杂特征采集 API 证据、或审查 `V2.1-A` 之后的建模改动时，必须先读取本文件与 `docs/cad_capability_matrix.md`。

## 架构边界

本轮严格保持既有链路，未做任何结构改动：

`CADModelSpec -> FeatureGraph -> BuildPlanCompiler -> FeatureHandlerRegistry -> FeatureHandler -> ISolidWorksFeatureAdapter -> GeometryValidator -> QualityGate`

- Handler **不知道 COM**。所有 SolidWorks API 调用都在 Adapter 层。
- 没有新增零件专用逻辑，没有 `switch (feature_type)`。
- 新增特征只是向 `FeatureHandlerRegistry` 注册，注册表本身未改结构。

## 新增 Feature Handler

| Feature | Handler | 计划操作 | Adapter 入口 |
|---|---|---|---|
| `fillet` | `FilletHandler` | `CreateFillet` | `ExecuteFilletAsync` |
| `chamfer` | `ChamferHandler` | `CreateChamfer` | `ExecuteChamferAsync` |
| `linear_pattern` | `LinearPatternHandler` | `CreateLinearPattern` | `ExecuteLinearPatternAsync` |
| `circular_pattern` | `CircularPatternHandler` | `CreateCircularPattern` | `ExecuteCircularPatternAsync` |
| `mirror` | `MirrorHandler` | `CreateMirror` | `ExecuteMirrorAsync` |

## 参数契约

| Feature | 参数 | 约束 |
|---|---|---|
| `fillet` | `radius_mm`、`edge_selection` | 半径为有限正数；`edge_selection` 为 `EdgeSelectionCriteria` 的 JSON（snake_case），在 `Validate` 阶段即试解 |
| `chamfer` | `distance_mm`、`angle_deg`、`edge_selection` | 距离为有限正数；角度在开区间 (0, 90)；`edge_selection` 为 `EdgeSelectionCriteria` 的 JSON，在 `Validate` 阶段即试解 |
| `linear_pattern` | `direction`、`instance_count`、`spacing_mm`、`seed_feature` | 方向限 x/y/z；实例数为 [2, 512] 整数；间距为有限正数；种子特征必须出现在 `referenced_features` |
| `circular_pattern` | `axis`、`instance_count`、`angle_deg`、`seed_feature` | 轴限 x/y/z；实例数为 [2, 512] 整数；角度在区间 (0, 360]；种子特征必须出现在 `referenced_features` |
| `mirror` | `mirror_plane`、`target_features` | 基准面限 FrontPlane/TopPlane/RightPlane；被镜像特征必须出现在 `referenced_features` |

所有 Handler 都拒绝未声明参数，超出档案的取值一律返回 `invalid_feature_parameter`。

## 证据状态

**五个特征全部为 `verified`。**

| Feature | SolidWorks API | 状态 |
|---|---|---|
| `fillet` | `IFeatureManager.FeatureFillet3` | `verified` |
| `chamfer` | `IFeatureManager.InsertFeatureChamfer` | `verified` |
| `linear_pattern` | `IFeatureManager.FeatureLinearPattern4` | `verified` |
| `circular_pattern` | `IFeatureManager.FeatureCircularPattern5` | `verified` |
| `mirror` | `IFeatureManager.InsertMirrorFeature2` | `verified` |

五个特征的取证记录：

| Feature | 诊断输入 | 诊断报告 | 体积变化 | 闭式解 |
|---|---|---|---|---|
| `fillet` | `feature_pipeline_plate_fillet.json` | `20260828_014622_5303003` | 减少 14.09 mm³ | — |
| `chamfer` | `feature_pipeline_plate_chamfer.json` | `20260828_014630_2660931` | 减少 33.5 mm³ | 33.51 ✓ |
| `linear_pattern` | `feature_pipeline_plate_linear_pattern.json` | `20260828_014638_3133239` | 减少 848.23 mm³ | 848.23 ✓ |
| `circular_pattern` | `feature_pipeline_plate_circular_pattern.json` | `20260828_014645_3608011` | 减少 848.23 mm³ | 848.23 ✓ |
| `mirror` | `feature_pipeline_plate_mirror.json` | `20260828_014652_1288900` | 减少 282.74 mm³ | 282.74 ✓ |

全部在 SOLIDWORKS 2023（31.5.0）上执行，三重判定（结果对象非空、重建通过、体积可测变化）均为 true。

**"体积变了"不足以判定成功**，因此每个特征都另做了两重独立复核：实测体积与闭式解对照证明
数量对，只读拓扑探针量产出零件的落点证明位置对。落点复核结果：

- `chamfer`：两条 `radius=6.00` 的 `plane+cone` 圆边与两条 `y=1.00` 的 `cylinder+cone` 圆边，
  而 `y=10` 一侧孔口未被波及——同时证明判据真的按位置区分（圆角取 `anchor_y_mm=10`，倒角取 `0`）
- `linear_pattern`：孔心 x = -30 / -10 / 10 / 30，间距正是声明的 20 mm
- `circular_pattern`：孔心 (20,0)、(0,-20)、(-20,0)、(0,20)，每 90 度一个
- `mirror`：新孔在 (x=-25, z=-15)，只有 x 变号——正是关于 YZ 面镜像应有的结果

五个 Handler 的源码与 `SolidWorksEdgeEnumerator.cs` 已全部纳入
`FeatureExecutionEvidencePolicy.BoundSourcePaths`——取证的特征必须同样受源码 revision 约束，
否则改动它不会让证据失效，等于给它开了一个别人没有的后门。

### 平台上仍未取证的能力

`revolve_boss` 仍为 `unverified`，真实执行在连接 COM 之前被拒绝，`shaft_basic` 因此继续 fail-closed。
它缺的是中心线草图能力（`SketchHandler` 目前只接受 line / rectangle / circle，
`RealSolidWorksFeatureAdapter` 也没有 `CreateCenterLine`），与本轮解决的实体引用缺口无关，
不在 V2.1-A 范围内。

## 共同阻塞点及其解除

五个特征卡在同一个根因上：**通用 FeatureGraph 无法表达稳定的实体引用**。
本机 SDK 反射确认这五个 API **一个几何引用参数都不接受**，只作用于调用前的当前选择集，
因此这不是"查文档"能解决的实现细节，而是模型缺口。

缺口已全部解除，且**五个特征共用同一套选择模型**，没有各造一套：

| 特征 | 需要的引用 | 解法 |
|---|---|---|
| 圆角、倒角 | 边 | `EdgeSelectionCriteria` 直接解出的边 |
| 线性阵列 | 方向 | 解出一条直线边，取其单位方向 |
| 圆周阵列 | 轴 | 解出一条圆边，取其法向（`CircleParams` 的 3-5 位，此前被丢弃） |
| 镜像 | 基准面 + 多选顺序 | 标准面按名解析；选择标记由创建后的特征类别回读验证 |

链路：声明判据 → 对实测拓扑求解（命中数不等于 `expected_count` 即拒绝）→
方向/轴与声明主轴做平行性交叉校验 → 按标记建立选择集 → 调用 API →
`GetTypeName2` 确认特征类别 → 体积测量判定。

其中两道闸是这套东西能被信任的理由：`ExpectedCount` 不符即拒绝，以及方向与声明主轴不平行即拒绝。
选错边或选错方向时这些 API 照样返回非空 `IFeature`，产出的是特征打在错误位置的零件。

### 走过的弯路：CreateDefinition 显式引用

SDK 里另有 `CreateDefinition` / `CreateFeature` 一条路，其 FeatureData 带
`D1Axis`、`Axis`、`Plane` 等显式引用属性，看起来能绕开选择标记。实测走不通：
新建定义对象的 `AccessSelections` 返回 false，不调它直接写引用则 `TrySetProperty`
不报错但读回来是 null。

这条弯路留下一条纪律：**设置成功不等于写进去了**。适配器的读回校验正是为此存在，
也正是它当场抓住了这次静默丢弃。

### 顺带修掉的两层矛盾

- `FeatureParameterSchemaRegistry` 的阵列判据要求参数名 `count`，而 Handler 声明的是
  `instance_count` 且拒绝未声明参数——两层互相矛盾，任何阵列规格都不可能同时通过。
  这个矛盾一直没暴露，只因为阵列从未真正执行过。
- 诊断 Runner 的 `DiagnosticFeatureTypes` 是硬编码白名单，新增特征时会静默拒绝。

## 自检字段

- `fillet_handler_registered`
- `chamfer_handler_registered`
- `linear_pattern_handler_registered`
- `circular_pattern_handler_registered`
- `mirror_handler_registered`
- `complex_feature_registry_supported`
- `feature_api_evidence_required`
- `unverified_feature_blocks_execution`
- `feature_library_documented`
- `feature_regression_tests_passed`
- `v2_1_a_documented`
- `markdown_chinese_check_passed`

判据全部为行为式：注册用反射解析并校验 schema 与 API 名称；dry-run 真实编译计划。不使用 `.cs` 源码字符串匹配。

`unverified_feature_blocks_execution` 是**双向**判据，且期望方向取自 Handler 自报的证据状态，而不是写死的特征清单——每补一块真机证据就要改一次判据，正是"保护随迁移消失"的老毛病：

- 自报 `unverified` 的：合法参数也必须被拒，且失败阶段必须是 `feature_api_unverified`
- 自报 `verified` 的：必须真的过得了自己的证据门（`AllowsRealExecution` 与 `ValidateEvidenceForRealExecution` 同时为真）

因此"只把状态改成 verified 却不补证据绑定"的伪装取证会在正向分支上失败，而不是悄悄放行。

## 验证标准

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

- 五个 Handler 全部注册且 schema 非空。
- 合法参数可通过 dry-run 编译出操作。
- 仍未取证的能力（`revolve_boss`）的合法参数在真实执行前仍被证据门拒绝。
- 五个复杂特征的合法参数均可通过证据门，且证据绑定完整（EvidenceId、诊断路径、源码 revision、SolidWorks 版本齐备）。
- 每个特征都有 `api_evidence.md` 与 `failure_repair.md`。

## 失败处理

见各特征目录下的 `failure_repair.md`。核心原则：`feature_api_unverified` 是**预期行为不是缺陷**，需按证据流程采集，不得通过调高状态或放宽校验来「修好」。

## 禁止事项

- 不得在无证据的情况下把任何特征状态改为 `verified`。五个特征的 `verified` 均由真机诊断报告支撑，改动其执行链源码后必须重采。
- 不得在 Handler 中直接调用 COM。
- 不得新增零件专用逻辑或 `switch (feature_type)`。
- 不得复制第三方脚本或宏。
- 本轮完成后不进入 V2.1-B。
