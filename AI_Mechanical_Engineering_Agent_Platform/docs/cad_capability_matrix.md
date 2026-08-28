# CAD 能力矩阵

## 目标

记录通用建模内核当前**真实**支持的 Feature 能力边界。本矩阵是判断一个零件族能否进入真实执行的唯一能力依据，不写计划中的能力，只写代码与证据已经支持的能力。

## 适用范围

新增零件族、扩展 FeatureGraph、评估真实执行可行性或审查 `V2.0-E` 之后的建模改动时，必须先读取本矩阵。

## 状态词表

| 状态 | 含义 |
|---|---|
| `Supported` | Handler 已实现，证据状态为 `Verified`，可进入真实执行 |
| `Partial` | Handler 已实现，但仅覆盖窄参数档案，超出档案即 fail-closed |
| `Unverified` | Handler 已实现，但缺少 Feature 级真实执行证据，真实执行被拒绝 |
| `Unsupported` | 尚无 Handler，`BuildPlanCompiler` 阶段即拒绝 |

## 能力矩阵

| Feature | Handler | SolidWorks API 状态 | 验证状态 | 当前限制 |
|---|---|---|---|---|
| `sketch` | `SketchHandler` | `CreateCircle`、`CreateLine`、`CreateCenterRectangle` 已实调 | `Supported` | 仅限标准基准面；仅直线、中心矩形、圆；不支持圆弧、槽、约束与尺寸 |
| `extrude_boss` | `ExtrudeBossHandler` | `FeatureExtrusion2` 已实调 | `Supported` | 仅 blind 单向拉伸；不支持 `mid_plane`、拔模、薄壁与多实体 |
| `extrude_cut` | `ExtrudeCutHandler` | `FeatureCut4` 已实调 | `Supported` | 仅 blind 切除；不支持 `through_all`、反向切除、薄壁与多实体范围 |
| `hole` | `HoleHandler` | `FeatureCut4` 配单圆草图 | `Partial` | 以圆草图加 blind 切除实现；依赖草图恰为一个等径圆；不使用 `SimpleHole2` 与 Hole Wizard |
| `revolve_boss` | `RevolveBossHandler` | `FeatureRevolve2` 已接线但无 Feature 级证据 | `Unverified` | 缺少独立 diagnostic 与几何复核，真实执行被证据策略拒绝；`shaft_basic` 因此 fail-closed |
| `revolve_cut` | 无 | 未接线 | `Unsupported` | 无 Handler，`FeatureHandlerRegistry` 无法解析 |
| `fillet` | `FilletHandler` | `FeatureFillet3` 已实调 | `Supported` | 仅等半径边圆角；边由 `EdgeSelectionCriteria` 判据求解，命中数不符即拒绝；不支持变半径、setback、面圆角与 conic |
| `chamfer` | `ChamferHandler` | `InsertFeatureChamfer` 已实调 | `Supported` | 仅距离-角度边倒角；边由 `EdgeSelectionCriteria` 判据求解，命中数不符即拒绝；不支持顶点倒角、等距倒角与切线延伸 |
| `linear_pattern` | `LinearPatternHandler` | `FeatureLinearPattern4` 已实调 | `Supported` | 仅单方向；方向边由判据求解并与声明主轴交叉校验；不支持第二方向、跳过实例与 VaryInstance |
| `circular_pattern` | `CircularPatternHandler` | `FeatureCircularPattern5` 已实调 | `Supported` | 仅等角单方向；轴由圆边法向求解并与声明主轴交叉校验；不支持双方向、对称与跳过实例 |
| `mirror` | `MirrorHandler` | `InsertMirrorFeature2` 已实调 | `Supported` | 仅关于标准基准面镜像特征；不支持镜像实体、镜像面与曲面缝合 |

## 实体引用模型

V2.1-A 新增的四个特征与既有的 `revolve_boss` 曾卡在同一个根因上：**通用 FeatureGraph 无法表达稳定的实体引用**。该缺口现已在模型层解决。

### 为什么这不是"查文档"能解决的

本机 SDK（`C:\Program Files\SOLIDWORKS2023\SOLIDWORKS\api\redist`）反射确认，这些 API **不接受任何几何引用参数**：

| API | 参数数 | 是否含几何引用 |
|---|---|---|
| `FeatureFillet3` | 14 | 无 |
| `InsertFeatureChamfer` | 8 | 无 |
| `InsertMirrorFeature2` | 5 | 无 |
| `FeatureLinearPattern4` | 20 | 仅 `DName1/2` 名称字符串 |
| `FeatureCircularPattern5` | 14 | 仅 `DName` / `DName2` 名称字符串 |

它们只作用于**当前选择集**。而选中一条边只有两条路：`SelectByID2` 依赖会漂移的自动生成名称或需先算出的坐标；`Select4` 要求已经持有实体对象。因此缺的是"如何在声明式图里稳定指认一条边"，属于拓扑命名问题。

### 已建立的机制

| 环节 | 位置 | 说明 |
|---|---|---|
| 判据声明 | `EdgeSelectionCriteria` | 按边类型、长度、半径、定位点、相邻面性质声明；CAD 无关，可独立单测 |
| 判据解析 | `EdgeSelectionCriteriaParser` | 解析 JSON（snake_case），并在 `Validate` 阶段对空拓扑试解一次，非法判据不拖到 COM 之后 |
| 拓扑枚举 | `SolidWorksEdgeEnumerator` | 由 `IBody2.GetEdges` 实测，同时产出 `MeasuredEdge` 纯数据与 COM 句柄；`SolidWorksGeometryReader.ReadEdges` 复用同一实现 |
| 求解 | `EdgeSelectionResolver` | 对实测边求解判据 |
| 安全闸 | 同上 | **匹配数不等于 `ExpectedCount` 即 fail-closed** |
| 选择集复核 | `RealSolidWorksFeatureAdapter.SelectEdgesByCriteria` | 逐条 `Select4` 后用 `GetSelectedObjectCount2(-1)` 反查选择集大小是否等于求解数量 |

`ExpectedCount` 那条闸是整套机制存在的理由：`FeatureFillet3` 在选错边时同样返回非空 `IFeature`，产出的是圆角打在错误位置的零件。宁可拒绝执行，也不允许在选择不确定时继续。

### 真机验证结论

对 `evidence/solidworks/20260824_072219_4629586/model.SLDPRT`（100×60×10 板，两个 Ø10 通孔）实测：

- 枚举出 16 条边：12 条直线 + 4 条孔口圆，与独立探查逐条一致
- 判据"圆边 + 圆柱面/平面相交 + `anchor_y=10`"解析出**恰好 2 条**顶面孔口
- 判据"圆边"（未加位置约束）匹配 4 条却声明要 1 条时，返回 `edge_selection_ambiguous` 拒绝执行

另有两条实测结论已固化为设计约束：

- `IEdge.GetID()` 对所有边返回 **0**，不具区分度，不可用于识别
- 闭合圆边**没有起止顶点**，因此定位点对圆边取圆心、对直线边取两端点中点

### 完成进度

V2.1-A 的五个复杂特征**全部完成真机取证**。

| Feature | API | 诊断报告 | 独立复核 |
|---|---|---|---|
| `fillet` | `FeatureFillet3` | `evidence/solidworks/20260828_014622_5303003/…` | 体积减少 14.09 立方毫米 |
| `chamfer` | `InsertFeatureChamfer` | `evidence/solidworks/20260828_014630_2660931/…` | 减少 33.5，与 Pappus 闭式解 33.51 一致；探针量到 radius=6.00 的 `plane+cone` 圆边 |
| `linear_pattern` | `FeatureLinearPattern4` | `evidence/solidworks/20260828_014638_3133239/…` | 减少 848.23，等于 3 个 Ø6 通孔；探针量到孔心 x = -30/-10/10/30 |
| `circular_pattern` | `FeatureCircularPattern5` | `evidence/solidworks/20260828_014645_3608011/…` | 减少 848.23；探针量到孔心 (20,0)、(0,-20)、(-20,0)、(0,20)，每 90 度一个 |
| `mirror` | `InsertMirrorFeature2` | `evidence/solidworks/20260828_014652_1288900/…` | 减少 282.74，等于 1 个 Ø6 通孔；探针量到新孔 (-25,-15)，只有 x 变号 |

每一条的判据都不是"体积变了"，而是**实测值与闭式解一致**加上**探针复核落点**。
前者证明数量对，后者证明位置对；只有两者同时成立才谈得上特征打对了地方。

### 从边判据到方向与轴

五个特征最终共用同一套选择模型，没有为每种特征各造一套引用机制：

| 需要什么 | 怎么来的 |
|---|---|
| 圆角、倒角的目标边 | `EdgeSelectionCriteria` 直接解出的边 |
| 线性阵列的方向 | 解出一条**直线边**，取其单位方向 |
| 圆周阵列的轴 | 解出一条**圆边**，取其法向（`ICurve.CircleParams` 的 3-5 位，此前一直被丢弃） |
| 镜像的基准面 | 标准基准面按名解析——这类名称稳定，不属于会漂移的自动生成名 |
| 阵列与镜像的种子 | 适配器内部按 `feature_id` 维护的 COM 句柄映射 |

方向与轴额外带一道**平行性交叉校验**：规格里声明的主轴（x/y/z）与解出的边方向必须
|cos| ≥ 0.999，否则拒绝执行。判据负责"选哪条"，声明负责"应该指向哪儿"，两者互相印证。
只有判据时，一条合法但不是设计者想要的边会让整排实例静默错位。

### 走过的弯路：CreateDefinition 显式引用

本机 SDK 显示另有 `IFeatureManager.CreateDefinition` / `CreateFeature` 一条路，
其 FeatureData 对象带 `D1Axis`、`Axis`、`Plane` 等**显式几何引用属性**，看起来能一举绕开选择标记。
实测走不通：对新建定义对象 `AccessSelections` 返回 false，不调它直接写引用则
`TrySetProperty` 不报错但属性**读回来是 null**。

这个弯路留下了一条有用的纪律：**设置成功不等于写进去了**。适配器里的读回校验就是为此存在的，
也正是它把这次静默丢弃当场抓了出来，而不是让一个没有方向引用的定义走到 `CreateFeature`。

## 零件族能力映射

| 零件族 | 所需 Feature | 可否真实执行 | 原因 |
|---|---|---|---|
| `plate_basic_4holes` | `extrude_boss`、`extrude_cut` | 可以 | 所需 Feature 全部 `Supported` |
| `flange_basic` | `extrude_boss`、`extrude_cut` | 可以 | 所需 Feature 全部 `Supported` |
| `jacket_basic` | `extrude_boss`、`extrude_cut` | 受零件族证据限制 | Feature 层可用，但零件族结构化运行时证据仍为 pending |
| `shaft_basic` | `revolve_boss` | **不可以** | `revolve_boss` 证据状态为 `Unverified`；Definition 与统一内核均 fail-closed |

## 几何校验能力

V2.0-D 的几何校验原本挂在零件专用 Builder 上。V2.0-E 统一执行路线后，生产端（`JacketFeatureBuilder`）不再注册、消费端（产物校验器的零件族分支）不再触达，该链路两端同时失联且无人察觉——因为夹套本就 fail-closed。

收尾时已把它提升为**平台级后置阶段**：

| 环节 | 位置 | 说明 |
|---|---|---|
| 期望值 | `IPartFamilyDefinition.DescribeExpectedGeometry` | 由零件族从自身参数推导，与 CAD API 无关 |
| 测量 | `ISolidWorksGeometryReader` | 通用读取，不含零件知识 |
| 判定 | `PartGeometryValidator` | 全平台唯一一份，可独立单测 |
| 执行 | `SolidWorksPartFamilyBuilderBase` 构建后、保存前 | 校验失败即 fail-closed，不落盘 |
| 复核 | `SolidWorksArtifactValidator.ValidateGeometryEvidence` | 由 build_report 自描述驱动，校验器不持有零件知识 |

| 零件族 | 是否声明几何期望 | 期望内容 |
|---|---|---|
| `jacket_basic` | 是 | 单实体；体积 `pi/4 * (Do^2 - Di^2) * L` |
| `plate_basic_4holes` | 否 | 尚未声明 |
| `flange_basic` | 否 | 尚未声明 |
| `shaft_basic` | 否 | 尚未声明 |

未声明期望的零件族会跳过几何校验，`build_report` 中记为 `geometry_validation_status = "NotDeclared"`。**这是一个可见缺口，不是默认通过**——跳过的事实会写进产物报告，可被审查发现。

## 基线治理规则

`docs/self_check_capability_baseline.json` 是受版本控制的能力契约，由 `SelfCheckCapabilityRegressionGate` 强制执行。

- 任何能力字段**首次为 true 之后**，必须同步加入基线，否则它不受回归保护。
- 基线中的值只能是 `true`。写 `false` 会被闸判为配置错误——因为削弱基线是绕过本闸门阻力最小的路径。
- 需要移除某项保护时必须显式删除字段并说明理由，不得就地改成 `false`。
- 受保护字段只增不减，由 `V20ERegressionGateTests` 锁定。
- 受保护字段必须同时接进自检快照；只加进基线不接线，闸会报配置错误而不是恒真通过。

## 执行步骤

1. 新增或修改 FeatureGraph 前，先在本矩阵确认所需 Feature 的验证状态。
2. 若所需 Feature 为 `Unverified` 或 `Unsupported`，不得声明该零件族可真实执行。
3. 需要把 `Unverified` 升级为 `Supported` 时，必须先在诊断 Runner 中采集 Feature 级证据，再回填 Handler 的 `FeatureApiEvidence`。
4. 每次 Handler 证据状态变化，必须同步更新本矩阵。

## 验证标准

- 本矩阵中的 `验证状态` 必须与 `FeatureHandler` 的 `ApiEvidence.Status` 一致。
- 本矩阵中标记为 `Supported` 的 Feature，必须能通过 `FeatureExecutionEvidencePolicy.ValidateEvidence`。
- self-check 中 `cad_capability_matrix_exists=true`。

## 失败处理

若真实执行返回 `feature_api_unverified`，先在本矩阵定位该 Feature 的验证状态：

- 状态为 `Unverified` 或 `Unsupported`：属预期 fail-closed，不是缺陷，需先补证据。
- 状态为 `Supported` 但仍被拒绝：说明证据链失效，按 `src/Workers/SolidWorks/failure_repair.md` 进入 API Evidence Driven Repair Loop。

## 禁止事项

- 不得在本矩阵中写入尚未接线的计划能力。
- 不得为了让零件族通过而调高验证状态。
- 不得绕过 `FeatureHandler` 直接在零件族或 Worker 中实现 Feature。
- 不得以文件存在、COM 返回非空或独立 diagnostic 作为把状态升为 `Supported` 的依据。
