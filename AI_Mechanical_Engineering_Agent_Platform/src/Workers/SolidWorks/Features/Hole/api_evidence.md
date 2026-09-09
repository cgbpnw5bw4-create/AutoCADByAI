# 孔特征 API 证据

## 目标与适用范围

维护 V2.1-B 的 `SimpleHole`、`CounterboreHole`、`CountersinkHole`、`TappedHole` 证据边界。四类定义复用唯一 `HoleHandler`；真实 API 只能在 `ISolidWorksFeatureAdapter` 边界内执行。定义、自动测试或官方签名存在，均不等于已获得真实执行能力。

## 输入与输出

输入为 `HoleFeatureDefinition`、精确参数档案、引用与选择状态、官方资料、本地 SDK、宏和同次诊断读数；输出为逐类型 API 证据、失败阶段、有效源码绑定和真实执行结论。字段必须包含 API 名称、来源、参数映射、单位、前置选择、返回值、验证状态、已知失败与宏证据。

## 四类证据状态

| 类型 | 选定或研究 API | 参数映射与单位 | 前置选择与返回值 | 已验证状态 | 宏录制证据 |
|---|---|---|---|---|---|
| `SimpleHole` | 历史 `ISketchManager.CreateCircle` + `IFeatureManager.FeatureCut4`；孔向导仅研究 `HoleWizard5` / `IWizardHoleFeatureData2` | 圆半径为 `diameter_mm/2/1000`，切深为 `depth_mm/1000`；盲切与 `through_all` 是不同档案 | 历史要求直接依赖恰好一个等径圆的草图并精确激活；返回圆段与切除 `IFeature`，空值或错误均失败 | 历史单圆盲切有真机证据；增强位置/面/数量/贯穿档案仍为 `unverified`；历史授权须通过当前源码绑定核验 | 历史用户“先拉伸后切除”宏和诊断支持普通切孔，不支持孔向导或螺纹 |
| `CounterboreHole` | 研究 `HoleWizard5` 的 `swWzdCounterBore` 或 `IWizardHoleFeatureData2` | 主孔径/深度用米；`Value1=counterbore_diameter_mm/1000`，`Value2=counterbore_depth_mm/1000`；属性为 `CounterBoreDiameter` / `CounterBoreDepth` | 选定有效放置面和孔中心；初始化定义后创建 Feature；返回非空 `IFeature` 仍需两级几何复核 | `unverified`；没有本轮参数映射与真机诊断授权 | 无 |
| `CountersinkHole` | 研究 `HoleWizard5` 的 `swWzdCounterSink` 或 `IWizardHoleFeatureData2` | 主孔径/深度用米；`Value1=countersink_diameter_mm/1000`，`Value2=countersink_angle_deg*pi/180`；属性为 `CounterSinkDiameter` / `CounterSinkAngle` | 放置面、孔中心、近侧语义和方向必须明确；返回 `IFeature` 后独立读取锥面、入口径和角度 | `unverified`；未完成锥面角度与终止条件真机读回 | 无 |
| `TappedHole` | 研究 `HoleWizard5` 的 `swWzdTap` 或 `IWizardHoleFeatureData2.InitializeHole` + `CreateFeature` | 标准/紧固件/尺寸须匹配本机数据库；`Value1=thread_depth_mm/1000`，`Value7=CosmeticThreadType`，`Value8=ThreadEndCondition`；底孔用 `TapDrillDiameter` / `TapDrillDepth`，标准/尺寸用 `Standard2` / `FastenerType2` / `FastenerSize` | 有效实体面、孔位和已初始化的攻丝孔定义；创建后读回孔向导类型、规格、深度与底孔元数据，非空 Feature 不是通过 | `unverified`；数据库映射、选择、创建和读回无独立真机授权 | 无；官方警告攻丝宏可能错录 `Length` 占位，宏也需逐参数复核 |

`thread_pitch` 由项目螺纹目录校验，并由未来标准/规格映射与读回确认；不得塞入 `HoleWizard5.Length`，该参数用于槽长。项目的 `ISO_METRIC` 不是 SolidWorks 数据库枚举，不能转换为猜测的数字。项目孔几何 `diameter_mm` 与 `tap_drill_diameter_mm` 表示底孔直径，螺纹公称径由 `thread_size` 表示；未来真实策略必须固定并验证装饰螺纹选项，不能混用不同几何语义。

## 官方与本地资料

- [CreateCircle 官方方法](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateCircle.html) 与 [FeatureCut4 官方方法](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureCut4.html)：历史普通几何孔的参数与返回合同。
- [HoleWizard5 官方方法](https://help.solidworks.com/2020/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~HoleWizard5.html)：四种孔类型、长参数表、单位和攻丝宏限制。
- [孔向导与 WizardHoleFeatureData2 官方指南](https://help.solidworks.com/2023/english/api/sldworksapiprogguide/Overview/Hole_Wizard_Features_and_WizardHoleFeatureData2_Objects.htm)：研究路线为 `CreateDefinition(swFmHoleWzd)` → `InitializeHole(type,standard,fastener,size,end)` → 类型属性 → 选择引用 → `CreateFeature`，尚无项目执行证据。
- [孔向导与草图点官方示例说明](https://help.solidworks.com/2023/English/api/sldworksapi/Create_Holes_Using_Hole_Wizard_and_Sketch_Points_Example_CSharp.htm)：用于查证孔位、实体面和模型/草图坐标转换，不复制示例脚本。
- [装饰螺纹官方说明](https://help.solidworks.com/2023/english/solidworks/sldworks/c_cosmetic_threads.htm)：用于区分表示与实体几何，以及有无装饰螺纹时攻丝孔直径语义。
- [建模螺纹官方 API 指南](https://help.solidworks.com/2023/English/api/sldworksapiprogguide/OVERVIEW/Thread_Features_and_ThreadFeatureData_Objects.htm)：`IThreadFeatureData` / `swFmSweepThread` 是独立能力，不在本阶段真实支持范围。
- 本机只读反射来源：`C:\Program Files\SOLIDWORKS2023\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll` 与 `SolidWorks.Interop.swconst.dll`，程序集版本 `31.5.0.52`。确认 `HoleWizard5` 有 27 个参数并返回 Feature，`InitializeHole` 为五参数无返回值，`CreateFeature` 返回 Feature；未调用 COM，不是实机通过。
- [Guhring 公制螺纹资料](https://guhring.com/media/catalogs/044mlrkbbek.pdf) 与 [Dormer Pramet 预钻孔说明](https://www.dormerpramet.com/uk-ie/en/which-size-should-I-pre-drill)：用于有限螺纹目录的规格与螺距依据；底孔还受工艺与材料影响，经验公式不能代替完整标准库。

## 攻丝孔的四层语义

| 层次 | 实际含义 | 本阶段边界 |
|---|---|---|
| 几何孔 | 圆与切除形成圆柱孔 | 普通 `SimpleHole` 几何能力，不携带已验证螺纹定义 |
| `Cosmetic Thread` | 螺纹表示或标注属性 | 不生成螺旋实体，不单独证明标准攻丝孔创建成功 |
| `Hole Wizard / Tapped Hole` | 标准、规格、螺距、有效深度和底孔绑定的孔向导特征 | `TappedHole` 的最低语义；真实 API 与元数据未取证时拒绝执行 |
| 真实建模螺纹 | 有螺旋牙型实体的独立螺纹特征 | 需独立 API、轮廓和几何证据，本轮不支持 |

## 历史证据与源码失效规则

入场时 `HoleHandler` 的历史绑定为 `v2.1-a-20260828-014622-simple-hole`，报告为 `evidence/solidworks/20260828_014622_5303003/feature_execution_report.json`，SolidWorks `31.5.0`，源码修订为 `feature-execution-source-sha256:533b14e951348372edee939de64e411663a94fbd2dc49ac60c18ee63c298cd54`。普通孔体积从 `5.9214601836602546E-05` 降至 `5.84292036732051E-05 m³`，只支持 `simple_circular_cut_blind;diameter_matches_single_circle;positive_depth_mm;no_wizard`。

更早的 `20260821_034143_9836278`、`20260730_085830_6592380` 仅保留审计背景；`20260730_073759_9143941` 因人工确认无孔而作废，非空返回或重建成功不得恢复其资格。

改变受绑定 Handler、Adapter 或执行链后，旧复合 `SourceRevision` 失效。没有在当前源码重新采证并通过策略前，历史 `verified` 不代表当前可执行。即使重新验证既有单圆盲切，也不会授权新的显式孔类型、面引用、数量、贯穿或攻丝档案。最终有效状态见 [能力矩阵](../../../../../docs/cad_capability_matrix.md)。

本轮已重新执行既有基础档案，报告为 `evidence/solidworks/v2_1_b_refresh/20260907_013221_3210868/feature_execution_report.json`，SolidWorks `31.5.0`，源码修订为 `feature-execution-source-sha256:e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09`。普通孔之后体积为 `58429.2036732051 mm³`，同目录只读探针记录四条孔口圆边，根代理独立核对孔位、半径、高度和闭式体积。状态为 `CandidatePassed`、`real_cad_executed=true`、`NotDeliverable`，只覆盖历史隐式单圆盲切，不验证 V2.1-B 显式四孔型。`HoleHandler` 当前证据标识为 `v2.1-b-20260907-refresh-HoleHandler`，已绑定上述新报告与源码修订；九个既有 Handler 及三圆策略完成独立绑定复核，最终全量测试 `449/449` 通过。全部七份重采路径、独立复核与验证限制见 [阶段重采记录](../../../../../docs/v2_1_b_hole_features.md#本轮既有参数档案真实重采)。

## 执行步骤与验证标准

1. 在 Worker 前验证孔类型、尺寸、螺纹目录、孔位、有效参考和完整依赖图，保留标准化请求。
2. 核对所选类型的 API、完整参数、单位、选择和返回合同；不足时保持 `unverified`，在连接 COM 前拒绝。
3. 如采证，隔离诊断须记录版本、源码哈希、参数档案、选择、返回、重建、独立几何与元数据、产物和失败阶段。模型坐标须查证 `ModelToSketchTransform` 转换方向，不能猜测。
4. 逐孔读取数量、中心、直径与轴向深度；沉孔读取两级同轴圆柱和台阶；沉头孔读取锥面、入口径及全角/半角；攻丝孔读回孔向导 Feature、标准、规格、深度与底孔。
5. 当前源码证据通过后才更新授权；诊断仍为 `CandidatePassed` / `NotDeliverable`，最终交付从 `run-cad-workflow` 经过 Validator、Reviewer、QualityGate。

## 常见失败与禁止事项

标准/紧固件/尺寸不匹配、无有效面或孔位、坐标/方向/终止错误、切除不与实体相交、COM 异常或空 Feature、重建失败、有特征无实际孔、孔向导元数据不一致均失败。阶段与下一步命令见 [孔失败修复说明](failure_repair.md)。

禁止 Handler COM、猜测数据库枚举、将螺距写入槽长、将普通 Cut 当攻丝孔、将装饰螺纹当实体螺纹、改写旧报告哈希冒充重采、复制第三方脚本或进入 V2.1-C。
