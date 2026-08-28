# 倒角 API 证据

## 目标

记录 `chamfer` Feature 的 SolidWorks API 证据状态。本文件是判断该 Feature 能否进入真实执行的依据。

## 适用范围

修改 `ChamferHandler`、扩展倒角参数档案，或改动边选择判据模型时必须先读取本文件。
本 Feature 已取证，因此任何改动都会让 `SourceRevision` 失效，必须重采证据。

## API 名称

`IFeatureManager.InsertFeatureChamfer`

## 输入参数

Handler 层参数：

- distance_mm — 倒角距离，单位毫米，必须为有限正数
- angle_deg — 倒角角度，开区间 (0, 90)
- edge_selection — 结构化边选择判据（JSON，snake_case），由 `EdgeSelectionCriteriaParser` 解析

`edge_selection` 与圆角完全同源，不是自由文本：

```json
{"kind":"circle","adjacent_surface_kinds":["cylinder","plane"],"anchor_y_mm":0,"expected_count":2}
```

判据在 `Validate` 阶段就做一次空拓扑试解，语法或约束非法立即失败。

Adapter 层需要传给 API 的参数（**取自本机 SDK 反射，非推测**）：

- `Options`（Int32，`swFeatureChamferOption_e` 位标志）、`ChamferType`（Int32，`swChamferType_e`）
- `Width`（Double，距离，单位米）、`Angle`（Double，角度，单位弧度）、`OtherDist`（Double）
- `VertexChamDist1` / `VertexChamDist2` / `VertexChamDist3`（Double，顶点倒角距离）

共 8 个参数，**全部是选项与数值，没有任何边或面参数**。目标边完全来自调用前的选择集。

## 返回结果

`IFeature` 对象表示成功，`null` 表示失败。**非空返回不等于几何正确**——必须配合重建结果与几何测量才能判定成功。

## 前置条件

- 目标边或面已被精确选中
- 距离与角度组合在相邻几何允许范围内
- 选择集与证据档案参数组合一致

## 已验证状态

`api_evidence_status = verified`

本机 SDK 反射确认（SOLIDWORKS 2023）：`InsertFeatureChamfer` 返回 `Feature`，8 个参数中不含几何引用。
使用的常量同样取自本机 `swconst`：`swChamferType_e.swChamferAngleDistance = 1`；
`Options` 取 0，即不启用 `swFeatureChamferOption_e` 的 flip(1)、keep-feature(2)、
切线延伸(4) 与 propagate-to-parts(8)。

V2.1-A 已在本机 SOLIDWORKS 2023 完成真实执行诊断：

| 项目 | 值 |
|---|---|
| 诊断输入 | `examples/feature_pipeline_plate_chamfer.json` |
| 诊断报告 | `evidence/solidworks/20260828_014630_2660931/feature_execution_report.json` |
| SolidWorks 版本 | 31.5.0 |
| `result_object_validated` | true |
| `rebuild_passed` | true |
| `geometry_change_validated` | true |
| 体积变化 | 5.84292036732051E-05 → 5.8395693351566806E-05 立方米（减少 33.5 立方毫米） |
| 参数档案 | `distance_angle_edge_chamfer;angle_distance_type;criteria_resolved_selection` |

### 几何被独立复核，而不是只看"体积变了"

体积变化只证明模型动过，不证明动对了。因此另做了两重复核：

1. **闭式解对照**：1 mm×45° 倒角在两条 Ø10 孔口上，按 Pappus 定理应削去
   `0.5 mm² × 2π × 5.333 mm × 2 = 33.51 mm³`。实测 33.5 mm³，四位有效数字一致。
2. **拓扑复核**：用只读探针 `tools/SolidWorksEdgeProbe` 读产出零件，量到
   两条 `radius=6.00` 的 `plane+cone` 圆边（5 mm 孔壁外扩 1 mm）与两条 `y=1.00`
   的 `cylinder+cone` 圆边（沿孔壁上移 1 mm），正是 1 mm×45° 锥面环应有的形状；
   而 `y=10` 一侧的两条孔口仍是 `cylinder+plane`，未被波及。

第 2 条同时证明了判据真的按位置区分：圆角样本用 `anchor_y_mm=10`，倒角样本用
`anchor_y_mm=0`，同一个零件的两组孔口只差定位点，各自命中各自那一组。

因此：

- `FeatureApiEvidence.Status` 为 `verified`，`AllowsRealExecution` 为 `true`
- `FeatureExecutionEvidencePolicy` 已把 `Chamfer/ChamferHandler.cs` 纳入 `BoundSourcePaths`
- `RealSolidWorksFeatureAdapter.ExecuteChamferAsync` 是真实 COM 实现

**dry-run、BuildPlan 与真实执行均可用，且仅限上表的参数档案。**

### 授权边界

已取证的只有上表这一个参数档案。以下**仍然禁止**，改动前必须重新取证：

- 顶点倒角（`swChamferVertex`）、等距倒角（`swChamferEqualDistance`）、距离-距离倒角
- 切线延伸与 flip 方向
- `edge_selection` 判据命中数与 `expected_count` 不一致时的任何"就近取一条"行为


## 卡点是什么，以及它是怎么解决的

上面的参数表来自本机 SDK 反射，不是记忆或推测。它恰好说明了卡点所在：**这些 API 不接受几何引用参数，只作用于调用前的当前选择集**。

选中目标边只有两条路，都不是查文档能解决的：

| 方式 | 需要什么 | 障碍 |
|---|---|---|
| `IModelDocExtension.SelectByID2(Name, Type, X, Y, Z, Append, Mark, Callout, SelectOption)` | 边的名称字符串，或边上一点的坐标 | SolidWorks 自动生成的名称（如 `Edge1@Boss-Extrude1`）在特征树变化后会漂移，即拓扑命名问题；坐标方式要求先算出边上的点 |
| `IEntity.Select4(Append, SelectData)` | 已经持有该边对象 | 对象只能由 `IBody2.GetEdges()` / `IFace2.GetEdges()` 枚举得到，再按几何条件判断哪一条是目标 |

因此真正缺的是**一套在声明式 FeatureGraph 中稳定指认拓扑实体的模型**：先声明几何判据，执行时对实测拓扑求解，再 `Select4` 打 mark。

该模型已由圆角建立，倒角**一字不改地复用**：`SolidWorksEdgeEnumerator` 实测拓扑 →
`EdgeSelectionResolver` 求解判据（命中数不等于 `expected_count` 即拒绝）→
适配器逐条 `Select4` 并用 `GetSelectedObjectCount2(-1)` 反查选择集大小 → 调用 API →
体积测量判定。倒角没有引入任何新的引用机制，这正是它能第二个取证的原因。

这也是为什么不能"先把 COM 调用写上去等以后验证"——选错边时 API 同样返回非空 `IFeature`，产出的是倒角打在错误位置的零件，属于本项目明令禁止的假成功。

## 已知失败模式

- 判据命中数不等于 `expected_count`：拒绝执行，不猜边
- 判据没有任何约束项或容差非法：`Validate` 阶段即失败
- 距离或角度超界返回 null 或生成无效实体
- 顶点倒角、等距倒角与切线延伸未授权

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
