# 线性阵列 API 证据

## 目标

记录 `linear_pattern` Feature 的 SolidWorks API 证据状态。本文件是判断该 Feature 能否进入真实执行的依据。

## 适用范围

修改 `LinearPatternHandler`、扩展 线性阵列参数档案，或改动边选择判据模型时必须先读取本文件。
本 Feature 已取证，因此任何改动都会让 `SourceRevision` 失效，必须重采证据。

## API 名称

`IFeatureManager.FeatureLinearPattern4`

## 输入参数

Handler 层参数：

- direction — x / y / z，**期望**的阵列方向主轴，与解出的边做平行性交叉校验
- direction_selection — 结构化边选择判据（JSON，snake_case），必须唯一命中一条直线边

```json
{"kind":"line","length_mm":100,"anchor_x_mm":0,"anchor_y_mm":10,"anchor_z_mm":30,"expected_count":1}
```
- instance_count — 整数，闭区间 [2, 512]，含种子特征
- spacing_mm — 相邻实例间距，有限正数
- seed_feature — 种子特征标识，必须出现在 referenced_features 中

Adapter 层需要传给 API 的参数（**取自本机 SDK 反射，非推测**）：

- `Num1` / `Spacing1`、`Num2` / `Spacing2`（两个方向的实例数与间距）
- `FlipDir1` / `FlipDir2`（Boolean，方向翻转）
- `DName1` / `DName2`（String，方向实体的**名称字符串**）
- `GeometryPattern` / `VaryInstance`（Boolean）
- `HasOffset1` / `HasOffset2`、`CtrlByNum1` / `CtrlByNum2`、`FromCentroid1` / `FromCentroid2`、`RevOffset1` / `RevOffset2`（Boolean）
- `Offset1` / `Offset2`（Double）

共 20 个参数。方向靠 `DName` **名称字符串**指定，**种子特征不在参数里**，完全来自调用前的选择集。

## 返回结果

`IFeature` 对象表示成功，`null` 表示失败。**非空返回不等于几何正确**——必须配合重建结果与几何测量才能判定成功。

## 前置条件

- 种子特征已存在且被精确选中
- 方向引用为已解析的边、轴或基准
- 实例数与间距不产生自相交或越界实体

## 已验证状态

`api_evidence_status = verified`

本机 SDK 反射确认（SOLIDWORKS 2023）：`FeatureLinearPattern4` 返回 `Feature`，20 个参数中只有 `DName1/2` 是名称字符串，无实体引用。

V2.1-A 已在本机 SOLIDWORKS 2023 完成真实执行诊断：

| 项目 | 值 |
|---|---|
| 诊断输入 | `examples/feature_pipeline_plate_linear_pattern.json` |
| 诊断报告 | `evidence/solidworks/20260828_014638_3133239/feature_execution_report.json` |
| SolidWorks 版本 | 31.5.0 |
| 三重判定 | 结果对象非空、重建通过、体积可测变化 |
| 参数档案 | `single_direction_linear_pattern;criteria_resolved_direction;principal_axis_cross_checked` |

### 几何被独立复核，而不是只看"体积变了"

诊断零件：100×60×10 板，种子 Ø6 通孔在 x=-30，声明沿 x 每 20 mm 一个、共 4 个实例。

1. **闭式解对照**：阵列应削去 3 个 Ø6 通孔，即 `3 × π × 3² × 10 = 848.23 mm³`。
   实测体积由 59717.26 降到 58869.03 立方毫米，差值 **848.23**，完全一致。
2. **落点复核**：只读探针在产出零件上量到四个孔口圆心
   **x = -30 / -10 / 10 / 30**（z 均为 0，半径均 3.00）——间距正是声明的 20 mm，
   方向正是解出的那条 100 mm 直线棱。方向若被错误解读，这一列坐标会立刻露馅。
3. **特征类别**：创建后 `IFeature.GetTypeName2()` 返回 `LPattern`。

因此：

- `FeatureApiEvidence.Status` 为 `verified`，`AllowsRealExecution` 为 `true`
- `FeatureExecutionEvidencePolicy` 已把 `Pattern/LinearPatternHandler.cs` 纳入 `BoundSourcePaths`
- `RealSolidWorksFeatureAdapter.ExecuteLinearPatternAsync` 是真实 COM 实现

### 授权边界

仅单方向阵列。第二方向（`Num2`/`Spacing2`）、跳过实例、`VaryInstance`、
从质心起算与偏移量均未取证，改动前必须重新采集。

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

- 方向引用模型不存在，无法表达稳定方向实体
- 间距过小时相邻实例相交，可能返回非 null 但实体错误
- 双方向阵列、跳过实例、VarySketch 未授权

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
