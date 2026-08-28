# 圆周阵列 API 证据

## 目标

记录 `circular_pattern` Feature 的 SolidWorks API 证据状态。本文件是判断该 Feature 能否进入真实执行的依据。

## 适用范围

修改 `CircularPatternHandler`、扩展 圆周阵列参数档案，或改动边选择判据模型时必须先读取本文件。
本 Feature 已取证，因此任何改动都会让 `SourceRevision` 失效，必须重采证据。

## API 名称

`IFeatureManager.FeatureCircularPattern5`

## 输入参数

Handler 层参数：

- axis — x / y / z，**期望**的阵列轴，与解出的圆边法向做平行性交叉校验
- axis_selection — 结构化边选择判据（JSON，snake_case），必须唯一命中一条圆边

```json
{"kind":"circle","radius_mm":5,"anchor_x_mm":0,"anchor_y_mm":10,"anchor_z_mm":0,"expected_count":1}
```
- instance_count — 整数，闭区间 [2, 512]，含种子特征
- angle_deg — 阵列总角度，区间 (0, 360]
- seed_feature — 种子特征标识，必须出现在 referenced_features 中

Adapter 层需要传给 API 的参数（**取自本机 SDK 反射，非推测**）：

- `Number` / `Spacing`（实例数与角度，单位弧度）
- `FlipDirection`（Boolean）、`DName`（String，轴实体的**名称字符串**）
- `GeometryPattern` / `EqualSpacing` / `VaryInstance` / `SyncSubAssemblies`（Boolean）
- `BDir2` / `BSymmetric`（Boolean，第二方向与对称）
- `Number2` / `Spacing2` / `DName2` / `EqualSpacing2`（第二方向参数）

共 14 个参数。轴靠 `DName` **名称字符串**指定，**种子特征不在参数里**，完全来自调用前的选择集。

## 返回结果

`IFeature` 对象表示成功，`null` 表示失败。**非空返回不等于几何正确**——必须配合重建结果与几何测量才能判定成功。

## 前置条件

- 种子特征已存在且被精确选中
- 旋转轴引用为已解析的轴、边或临时轴
- 角度与实例数组合不产生重叠实例

## 已验证状态

`api_evidence_status = verified`

本机 SDK 反射确认（SOLIDWORKS 2023）：`FeatureCircularPattern5` 返回 `Feature`，14 个参数中只有 `DName` / `DName2` 是名称字符串，无实体引用。

V2.1-A 已在本机 SOLIDWORKS 2023 完成真实执行诊断：

| 项目 | 值 |
|---|---|
| 诊断输入 | `examples/feature_pipeline_plate_circular_pattern.json` |
| 诊断报告 | `evidence/solidworks/20260828_014645_3608011/feature_execution_report.json` |
| SolidWorks 版本 | 31.5.0 |
| 三重判定 | 结果对象非空、重建通过、体积可测变化 |
| 参数档案 | `principal_axis_circular_pattern;equal_spacing;criteria_resolved_axis` |

### 几何被独立复核，而不是只看"体积变了"

诊断零件：100×60×10 板，中心 Ø10 孔提供轴，种子 Ø6 通孔在 r=20 处，声明 4 个实例等分 360 度。

1. **闭式解对照**：阵列应削去 3 个 Ø6 通孔，即 848.23 mm³。
   实测由 58931.86 降到 58083.63 立方毫米，差值 **848.23**，完全一致。
2. **落点复核**：只读探针量到四个 Ø6 孔口圆心
   **(20, 0)、(0, -20)、(-20, 0)、(0, 20)**——恰好每 90 度一个，环绕中心孔轴。
   轴若被错误解读，实例会绕着别的地方转。
3. **特征类别**：创建后 `IFeature.GetTypeName2()` 返回 `CirPattern`。

轴来自哪里值得单独说明：圆边的 `ICurve.CircleParams` 是 7 个 double，
0-2 是圆心、**3-5 是轴方向**、6 是半径。项目原先只取了圆心和半径，把轴丢掉了；
把它读回来，圆周阵列要的"轴引用"就不必另建模型。

因此：

- `FeatureApiEvidence.Status` 为 `verified`，`AllowsRealExecution` 为 `true`
- `FeatureExecutionEvidencePolicy` 已把 `Pattern/CircularPatternHandler.cs` 纳入 `BoundSourcePaths`
- `RealSolidWorksFeatureAdapter.ExecuteCircularPatternAsync` 是真实 COM 实现

### 授权边界

仅等角单方向阵列。双方向（`BDir2`）、对称（`BSymmetric`）、跳过实例与
`VaryInstance` 均未取证，改动前必须重新采集。

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

- 轴引用模型不存在，无法表达稳定轴实体
- 等角分布与总角度语义易混淆，误用产生重叠或缺口
- 跳过实例与 VarySketch 未授权

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
