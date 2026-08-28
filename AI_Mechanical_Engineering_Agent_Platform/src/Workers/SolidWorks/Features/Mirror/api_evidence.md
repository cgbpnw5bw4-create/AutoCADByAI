# 镜像 API 证据

## 目标

记录 `mirror` Feature 的 SolidWorks API 证据状态。本文件是判断该 Feature 能否进入真实执行的依据。

## 适用范围

修改 `MirrorHandler`、扩展 镜像参数档案，或改动边选择判据模型时必须先读取本文件。
本 Feature 已取证，因此任何改动都会让 `SourceRevision` 失效，必须重采证据。

## API 名称

`IFeatureManager.InsertMirrorFeature2`

## 输入参数

Handler 层参数：

- mirror_plane — FrontPlane / TopPlane / RightPlane，仅标准基准面
- target_features — 被镜像特征标识，分号或逗号分隔，必须出现在 referenced_features 中

Adapter 层需要传给 API 的参数（**取自本机 SDK 反射，非推测**）：

- `BMirrorBody`（Boolean，是否镜像实体）
- `BGeometryPattern`（Boolean，几何阵列）
- `BMerge`（Boolean，合并结果）
- `BKnit`（Boolean，缝合曲面）
- `ScopeOptions`（Int32，特征范围选项）

**只有 5 个参数，全部是布尔与选项。镜像基准面与被镜像特征都不在参数里**，完全来自调用前的选择集（需以不同 mark 选中）。

## 返回结果

`IFeature` 对象表示成功，`null` 表示失败。**非空返回不等于几何正确**——必须配合重建结果与几何测量才能判定成功。

## 前置条件

- 镜像基准面与全部被镜像特征均已被精确选中
- 被镜像特征在特征树中位于镜像特征之前
- 镜像结果不与原实体自相交

## 已验证状态

`api_evidence_status = verified`

本机 SDK 反射确认（SOLIDWORKS 2023）：`InsertMirrorFeature2(BMirrorBody, BGeometryPattern, BMerge, BKnit, ScopeOptions)` 返回 `Feature`，5 个参数全部是选项，无任何几何引用。

V2.1-A 已在本机 SOLIDWORKS 2023 完成真实执行诊断：

| 项目 | 值 |
|---|---|
| 诊断输入 | `examples/feature_pipeline_plate_mirror.json` |
| 诊断报告 | `evidence/solidworks/20260828_014652_1288900/feature_execution_report.json` |
| SolidWorks 版本 | 31.5.0 |
| 三重判定 | 结果对象非空、重建通过、体积可测变化 |
| 参数档案 | `standard_plane_feature_mirror;right_plane_verified` |

### 几何被独立复核，而不是只看"体积变了"

诊断零件：100×60×10 板，种子 Ø6 通孔在 (x=25, z=-15)，关于 `RightPlane` 镜像。
两个坐标都刻意不为零——镜像面若选错，新孔会出现在别处。

1. **闭式解对照**：镜像应削去 1 个 Ø6 通孔，即 `π × 3² × 10 = 282.74 mm³`。
   实测由 59717.26 降到 59434.51 立方毫米，差值 **282.74**，完全一致。
2. **落点复核**：只读探针量到新孔位于 **(x=-25, z=-15)**——只有 x 变号、z 不变，
   正是关于 YZ 面（`RightPlane`）镜像应有的结果。
3. **特征类别**：创建后 `IFeature.GetTypeName2()` 返回 `MirrorPattern`。

基准面按标准名在特征树中解析为 `RefPlane` 对象。这里按名查找是安全的：
漂移的是 `Edge1@Boss-Extrude1` 那类自动生成名，标准基准面名是稳定的。

因此：

- `FeatureApiEvidence.Status` 为 `verified`，`AllowsRealExecution` 为 `true`
- `FeatureExecutionEvidencePolicy` 已把 `Mirror/MirrorHandler.cs` 纳入 `BoundSourcePaths`
- `RealSolidWorksFeatureAdapter.ExecuteMirrorAsync` 是真实 COM 实现

### 授权边界

仅关于标准基准面镜像特征。镜像实体（`BMirrorBody`）、镜像面、曲面缝合（`BKnit`）
与非标准基准面均未取证，改动前必须重新采集。

## 卡点是什么，以及它是怎么解决的

上面的参数表来自本机 SDK 反射，不是记忆或推测。它恰好说明了卡点所在：**这些 API 不接受几何引用参数，只作用于调用前的当前选择集**，并且要用**标记**区分选中项的角色。

### 走过的弯路：CreateDefinition 显式引用

本机 SDK 反射显示 `IFeatureManager` 另有一条 `CreateDefinition(Type)` / `CreateFeature(FeatureData)` 路线，
而 `ILinearPatternFeatureData.D1Axis`、`ICircularPatternFeatureData.Axis`、`IMirrorPatternFeatureData.Plane`
都是**显式的几何引用属性**——看起来能一举绕开标记问题。

实测结论是这条路在"新建"场景走不通：

- 对 `CreateDefinition` 返回的定义对象调用 `AccessSelections(model, null)` 返回 **false**
- 不调用 `AccessSelections` 直接写 `D1Axis`，`TrySetProperty` 不报错，但属性**读回来是 null**——写入被静默丢弃

正是"设置成功、读回为空"这一点让适配器里的读回校验有了存在意义。若当初信了 `TrySetProperty` 的返回值，
就会带着一个没有方向引用的定义去 `CreateFeature`，产出的可能是沿默认方向铺开的错误阵列。

### 实际采用的解法

退回选择集路线，但把"标记值是否被按预期解读"变成每次执行都验证的事实：

1. `SolidWorksEdgeEnumerator` 实测拓扑，`EdgeSelectionResolver` 按判据解出唯一一条边（`expected_count` 必须为 1）
2. 解出的边方向与规格里声明的主轴做**平行性交叉校验**（|cos| ≥ 0.999），不平行即拒绝执行
3. 种子特征按 `feature_id` 从适配器自己维护的映射取回 COM 句柄，取不到即失败
4. 按标记建立选择集，调用 API
5. 创建后用 `IFeature.GetTypeName2()` 确认 SolidWorks 建出的确实是预期类别的特征
6. 用体积测量判定几何真的变了

第 2 步是这套东西与"选中一条边就开工"的区别所在：判据负责"选哪条"，声明的主轴负责"应该指向哪儿"，
两者必须互相印证。阵列方向错一点，整排实例的位置就全错——而且错得很像成功。

`AccessSelections` 在本机后期绑定下对**已归属特征**的定义对象同样返回 false，因此运行期无法回读
`D1Axis` / `Axis` / `Plane` 做更强的引用校验。实例落点的正确性改由采证时的只读拓扑探针独立复核，
并由 `SourceRevision` 绑定锁住：执行链源码一改，证据即失效。

## 已知失败模式

- 多选选择集顺序敏感，模型尚未建立
- 被镜像特征晚于镜像特征生成时重建失败
- 镜像实体、面与曲面缝合未授权

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
