# 圆角 API 证据

## 目标

记录 `fillet` Feature 的 SolidWorks API 证据状态。本文件是判断该 Feature 能否进入真实执行的依据。

## 适用范围

修改 `FilletHandler`、扩展圆角参数档案，或改动边选择判据模型时必须先读取本文件。
本 Feature 已取证，因此任何改动都会让 `SourceRevision` 失效，必须重采证据。

## API 名称

`IFeatureManager.FeatureFillet3`

## 输入参数

Handler 层参数：

- radius_mm — 圆角半径，单位毫米，必须为有限正数
- edge_selection — 结构化边选择判据（JSON，snake_case），由 `EdgeSelectionCriteriaParser` 解析

`edge_selection` 不是自由文本，而是 `EdgeSelectionCriteria` 的序列化形式：

```json
{"kind":"circle","adjacent_surface_kinds":["cylinder","plane"],"anchor_y_mm":10,"expected_count":2}
```

判据在 `Validate` 阶段就做一次空拓扑试解，语法或约束非法立即失败，
不会拖到连接 COM 之后才暴露。

Adapter 层需要传给 API 的参数（**取自本机 SDK 反射，非推测**）：

- `Options`（Int32）、`R1` / `R2`（Double，半径，单位米）、`Rho`（Double）
- `Ftyp`（Int32，圆角类型）、`OverflowType`（Int32）、`ConicRhoType`（Int32）
- `Radii` / `Dist2Arr` / `RhoArr` / `SetBackDistances`（Object 数组，变半径与 setback）
- `PointRadiusArray` / `PointDist2Array` / `PointRhoArray`（Object 数组）

共 14 个参数，**全部是选项与数值，没有任何边或面参数**。目标边完全来自调用前的选择集。

## 返回结果

`IFeature` 对象表示成功，`null` 表示失败。**非空返回不等于几何正确**——必须配合重建结果与几何测量才能判定成功。

## 前置条件

- 目标边或面已被精确选中
- 半径小于相邻几何允许的最大值
- 选择集与证据档案参数组合一致

## 已验证状态

`api_evidence_status = verified`

本机 SDK 反射确认（SOLIDWORKS 2023，`SolidWorks.Interop.sldworks.dll`）：`FeatureFillet3` 返回 `Object`，14 个参数中不含几何引用。

V2.1-A 已在本机 SOLIDWORKS 2023 完成真实执行诊断：

| 项目 | 值 |
|---|---|
| 诊断输入 | `examples/feature_pipeline_plate_fillet.json` |
| 诊断报告 | `evidence/solidworks/20260828_014622_5303003/feature_execution_report.json` |
| SolidWorks 版本 | 31.5.0 |
| `result_object_validated` | true |
| `rebuild_passed` | true |
| `geometry_change_validated` | true |
| 体积变化 | 5.84292036732051E-05 → 5.8415117471027875E-05 立方米 |
| 参数档案 | `constant_radius_edge_fillet;uniform_radius;simple_type;criteria_resolved_selection` |
| 源码 revision | `feature-execution-source-sha256:71f3a03…287c43` |

体积**减少**而不是增加：判据命中的两条边是顶面孔口的凸边，圆角在此处削料。
适配器因此使用 `VolumeChangeExpectation.Changed`——只要求可测变化，不预设方向，
因为方向取决于边的凹凸性，写死方向会在凹边上产生假失败。

因此：

- `FeatureApiEvidence.Status` 为 `verified`，`AllowsRealExecution` 为 `true`
- `FeatureExecutionEvidencePolicy` 已把 `Fillet/FilletHandler.cs` 与
  `SolidWorksEdgeEnumerator.cs` 纳入 `BoundSourcePaths`，改动其一即让证据失效
- `RealSolidWorksFeatureAdapter.ExecuteFilletAsync` 是真实 COM 实现

**dry-run、BuildPlan 与真实执行均可用，且仅限上表的参数档案。**

### 授权边界

已取证的只有上表这一个参数档案。以下**仍然禁止**，改动前必须重新取证：

- 变半径、setback、面圆角、conic 圆角
- `edge_selection` 判据命中数与 `expected_count` 不一致时的任何"就近取一条"行为
- 把 `Ftyp` / `Options` 改成上表以外的常量组合


## 卡点是什么，以及它是怎么解决的

上面的参数表来自本机 SDK 反射，不是记忆或推测。它恰好说明了卡点所在：**这些 API 不接受几何引用参数，只作用于调用前的当前选择集**。

选中目标边只有两条路，都不是查文档能解决的：

| 方式 | 需要什么 | 障碍 |
|---|---|---|
| `IModelDocExtension.SelectByID2(Name, Type, X, Y, Z, Append, Mark, Callout, SelectOption)` | 边的名称字符串，或边上一点的坐标 | SolidWorks 自动生成的名称（如 `Edge1@Boss-Extrude1`）在特征树变化后会漂移，即拓扑命名问题；坐标方式要求先算出边上的点 |
| `IEntity.Select4(Append, SelectData)` | 已经持有该边对象 | 对象只能由 `IBody2.GetEdges()` / `IFace2.GetEdges()` 枚举得到，再按几何条件判断哪一条是目标 |

因此真正缺的是**一套在声明式 FeatureGraph 中稳定指认拓扑实体的模型**：先声明几何判据，执行时对实测拓扑求解，再 `Select4` 打 mark。

V2.1-A 就是这么做的，链路为：

1. `SolidWorksEdgeEnumerator` 从 `IBody2.GetEdges()` 实测每条边的种类、长度、半径、锚点与相邻面种类，产出 `MeasuredEdge`
2. `EdgeSelectionResolver` 拿判据对实测拓扑求解；命中数不等于 `expected_count` 时**拒绝执行**，错误信息里写明 "refusing to guess"
3. 适配器对求解出的每条边取其 COM 句柄 `Select4(true, selectData)`，再用 `GetSelectedObjectCount2(-1)` 反查选择集大小是否等于求解数量
4. 调用 `FeatureFillet3`，最后用体积测量而不是返回值判定成功

这也是为什么不能"先把 COM 调用写上去等以后验证"——选错边时 API 同样返回非空 `IFeature`，产出的是圆角打在错误位置的零件，属于本项目明令禁止的假成功。第 2 步和第 3 步就是为了让"选错边"无法悄悄发生。

`GetID()` 在本机返回 0 对所有边都成立，因此**不能**用作边的持久标识；
圆边没有顶点，所以锚点取自边的几何而非端点。这两条都是探针实测结论，
记录在 `SolidWorksEdgeEnumerator` 的注释里。

## 已知失败模式

- 判据命中数不等于 `expected_count`：拒绝执行，不猜边
- 判据没有任何约束项或容差非法：`Validate` 阶段即失败
- 半径过大返回 null 或产生自相交实体
- 变半径、setback、面圆角均未授权

## 重新取证的条件

本 Feature 已 verified。以下任一改动都会让证据失效，必须重跑一次真机诊断并重新盖章；
下列条件必须全部满足，缺一不可：

1. 在诊断 Runner 中完成一次真实执行，产出 `feature_execution_report.json`
2. 报告中该 Feature 的 `result_object_validated`、`rebuild_passed`、`geometry_change_validated` 均为 true
3. 几何变化经独立测量确认，不以 API 返回非空作为依据
4. 证据绑定运行时 SolidWorks 版本与执行链源码 revision
5. `RealSolidWorksFeatureAdapter` 中的真实 COM 实现随同证据一起提交
6. 同步更新 `docs/cad_capability_matrix.md`

## 禁止事项

- 不得在无证据的情况下把 `Status` 改为 `verified`；平台自检的
  `unverified_feature_blocks_execution` 判据是双向的，伪装取证会在正向分支上失败。
- 不得以「API 返回非空」作为验证通过的依据。
- 不得在 Handler 中直接调用 COM。
- 不得复制第三方脚本或宏。
