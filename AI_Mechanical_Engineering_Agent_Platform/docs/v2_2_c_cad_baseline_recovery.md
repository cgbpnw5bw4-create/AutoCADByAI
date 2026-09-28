# V2.2-C CAD 基线恢复与阵列证据重采

## 目标、适用范围与当前状态

本阶段在 `AI Mechanical Engineer Platform` 的 V2.2-B 架构成果上恢复既有 CAD 基线，处理已知物理证据失配、阵列种子绑定和带符号方向问题，然后重新采集来源一致的诊断与主流程证据。保留 `ModelRuntime`、`IWorkflowEngine`、默认规划输入校验、确定性 Worker 与扩展 QualityGate，不继续扩大平台架构。

**本阶段限定的 CAD 基线恢复已于 2026-09-14 完成，2026-09-24 进行文档收尾与复核。** 最终构建 0 警告、0 错误，完整测试 684/684 通过，原 8 项失败全部消除；四方向和七组生产候选完成两格式独立验收，参数更新正式主流程为 `Passed` / `Deliverable`，发布件实物复核通过。见 [最终验证汇总](../output/validation/v2_2_c_cad_baseline/verification_summary_20260914.json)。

同次全局 self-check 仍为 `Failed`：夹套生产证据与真实执行未激活，轴类真实工作流自检亦未通过。本阶段没有开放这些未获有效证据的能力。9 月 24 日文件保留和源码指纹复核通过，重新构建 0 警告、0 错误，完整测试仍为 684/684 通过；本次 self-check 退出码 2，恢复相关证据与能力回归门禁通过，全局仍保留上述既有缺口，中文 Markdown 检查通过。见 [本次验证汇总](../output/validation/v2_2_c_cad_baseline/verification_summary_20260924.json)。旧报告、拓扑验收、失败候选和采集清单保留原始记录，不能用最终成功改写中间失败。

本阶段范围包含受控真实 CAD 诊断和参数更新主流程；V2.2-B 的不启动真实 CAD 限定属于前阶段。当前仍遵守 `Feature Registry → Verified Handler → SolidWorks API Evidence → Worker → SolidWorks Adapter`，self-check、单元测试与 CI 不连接 COM。

## 输入与输出

输入为 V2.2-B 结束时的代码和 591 项测试基线、历史 8 项失败、7 组 `v2_1_b_refresh` 物理证据、当前运行环境、官方 API 资料、阵列反例与只读拓扑测量。输出为必要的最小规则修复、保留失败历史的新候选报告、独立方向/落点验收、重新绑定的受证能力以及参数更新主流程和完整验证记录。

独立验证目录为 `output/validation/v2_2_c_cad_baseline/`。环境候选位于 `evidence/solidworks/v2_2_c_environment_probe/`，方向候选位于 `evidence/solidworks/v2_2_c_direction_validation/`，七组最终生产候选已重采到 `evidence/solidworks/v2_2_c_refresh/`。这些目录也包含失败或中间候选，授权仅指向下文列出的最终新报告，不以目录整体表示通过。

## 已确认的基线与环境事实

| 项目 | 已确认事实 | 不能据此推断 |
|---|---|---|
| 修改前完整测试 | 总数 591，通过 583，失败 8 | 这是修复前基线；最终 684 项结果另列 |
| 旧 SLDPRT 完整性 | 7 组旧 `model.SLDPRT` 均比同组报告的 `size_bytes` 多 4096 字节 | 不能只把报告长度增加 4096 字节修成通过 |
| 旧 STEP 字节完整性 | 7/7 与对应记录一致 | 长度和 hash 一致不能证明 STEP 与对应 SLDPRT 是同一几何 |
| 9 月 9 日保护组件观察 | 当时 `LeaderTerm` 与 `LdMFilter` 运行 | 历史文件变更原因未定，不能归因为这些组件，也不能作为 9 月 24 日实时状态 |
| 9 月 9 日新环境探测 | 产生 `CandidatePassed`；模型实际 73488 字节，与报告一致，且可重新打开测量 | 单个新模型正常不能证明历史数据已恢复或后续全部档案有效 |

环境候选的 [执行报告](../evidence/solidworks/v2_2_c_environment_probe/20260909_072840_7316264/feature_execution_report.json) 记录 SolidWorks `31.5.0`，同目录 [只读拓扑报告](../evidence/solidworks/v2_2_c_environment_probe/20260909_072840_7316264/probe.json) 可用于核对重新打开后实际边与孔口。该报告仍为候选语义，不作为最终可交付结论。

当前候选诊断采用 `feature_execution_report.json`。工作区未发现可作为本轮真实来源的非 self-check `diagnostic_report.json`，不得拿自检夹具或旧格式说明替代这些实际候选报告。

### 2026-09-14 中断后复核与新增发现

[中断后核验报告](../output/validation/v2_2_c_cad_baseline/resume_20260914_verification.json) 对照两批候选共 44 个报告、原生探针、SLDPRT 和 STEP 文件，以及 180 个旧保护文件，均未发现漂移。该记录证明所核文件在中断前后保持一致，不能证明其中 STEP 的工程内容正确，也没有查明更早的物理失配原因。

9 月 9 日四个方向和七组生产候选的 SLDPRT 及原生模型探针反映了各自正确几何，但导出的 STEP 全部仍是同一个旧两孔 Φ10 几何。原先采集流程仅检查文件存在、长度、hash 稳定及原生模型拓扑，未证明 STEP 与本次模型一致，因此此前的 `CandidateCollected` 与原生拓扑 `Passed` 只能保留为当时检查范围内的结果，不能升级为生产授权。

生产证据绑定不接纳这批 STEP；修改导出相关源码后，旧 revision 自然不再授权新代码，最终已重新采集。禁止修改 9 月 9 日旧报告、`topology_acceptance.json` 或 manifest 来追认通过。

## 本轮最小修复范围

### 单种子与图依赖

线性和圆周阵列的 `seed_feature` 必须是一个非空标识；禁止逗号或分号分隔、多目标、自引用，以及省略或不匹配的 `referenced_features`。`referenced_features` 必须仅包含同一个种子，并参与依赖排序。运行前应确认该种子是当前图中真实的前序特征，不能借用报告名称或无关依赖冒充。

本轮发现 `FeatureHandlerPlanAdapter` 在 BuildPlan 到执行适配时丢失引用，使新增严格校验拒绝原本声明了种子的输入。首个反向边候选已在 COM 连接前以 `invalid_feature_parameter` 停止，报告中 `solid_works_connected=false`、`real_cad_executed=false`；[该失败报告](../evidence/solidworks/v2_2_c_direction_validation/20260909_073601_0292174/feature_execution_report.json) 保留。

修复已贯通编译、BuildPlan operation 和执行 FeatureDefinition 的引用，并补充直接种子 operation 与图绑定，未放宽 `referenced_features` 要求。修复后四个方向候选均完成最终采集与独立验收；完整测试结果见下文。

### 带符号方向与过原点主轴

线性方向与圆周轴采用类型化主轴描述和实际边方向的带符号点积决定 `Flip`，不再用绝对值丢掉反向信息。真机方向仍须用反向边和正、负声明轴的实际落点验证，不能只看体积或翻转布尔值。

圆周阵列声明过原点主轴时，选择条件必须明确主轴横向的两个坐标为零，不能以偏心平行圆边替代。选择后独立同轴复核容差不超过 0.05 mm；用户可以收紧，不能通过放大选择容差绕过同轴约束。

### 来源指纹与证据重采

将 `src/DomainSchemas/EdgeSelection.cs` 与 `src/Workers/SolidWorks/Features/Pattern/PatternParameterRules.cs` 纳入 `FeatureExecutionEvidencePolicy` 的源码绑定；STEP 修复进一步绑定 `src/Workers/SolidWorks/SolidWorksPlateBuilder.cs`、`src/Workers/SolidWorks/SolidWorksComInterop.cs` 和负责本次文档清理归属的 `src/Workers/SolidWorks/RealSolidWorksWorker.cs`，最终覆盖 31 个来源文件。影响方向、选择、种子、导出或执行的源码变化后，旧候选 revision 不再授权新源码；必须按原有复合指纹算法在最终代码上重采。

7 组新生产候选分别覆盖基础特征、V2.0-D 参数更新候选、圆角、倒角、线性阵列、圆周阵列和镜像。候选通过、物理一致、独立几何通过和正式主流程通过分别记录；不通过改写旧报告的 revision、长度或 hash 重新绑定。

### STEP 导出身份与两种格式的几何一致性

本次修复覆盖零件族与旧 plate 导出路径共享的 STEP 身份检查。原模型、激活返回对象和 `ActiveDoc` 必须指向同次完整路径，激活和导出错误码必须为零；清空选择异常立即终止，STEP 导出不再回退。中间连续采集证明“路径相同便跳过 `ActivateDoc3`”不足，最终改为内部保存使用唯一 basename、导出前总是激活并核对正确路径，只关闭本次拥有且已保存的模型，再复制为原有对外规范文件名。Worker 取消按固定名称猜测并关闭文档的行为。

已实测确认的参数根因是 `IModelDocExtension.SaveAs` 的 `ExportData` 需要按 `IDispatch` 传递，而旧反射调用使用裸 `null` 且没有将 `Errors`、`Warnings` 标为 byref，返回 `0x80020005 (DISP_E_TYPEMISMATCH)`。旧回退路径没有可靠关闭失败，产生了错误 STEP。修改为 `DispatchWrapper(null)` 并按 byref 传递两个输出参数后，隔离导出返回成功、无错误或警告。该 COM 修复只是必要部分，连续执行还需要上述唯一内部文件名、强制激活和文档归属约束。

[修复前隔离日志](../output/validation/v2_2_c_cad_baseline/export_isolation_20260914.log) 保留类型不匹配；[修复后隔离日志](../output/validation/v2_2_c_cad_baseline/export_isolation_dispatch_20260914.log) 和 [STEP 重导入探针](../output/validation/v2_2_c_cad_baseline/export_isolation_dispatch_20260914/step_probe.json) 显示单实体、三孔直径 6 mm、孔口 X 为 30/15/0 mm。原生模型体积 `59151.76998353076` mm³，重导入 STEP 为 `59151.769983530765` mm³，外包络和孔位一致。导入可能拆分重合圆柱面或圆边，比较按几何位置归并，不能直接用原始面/边数量计孔。

验收探针已增加通过 `LoadFile4` 重新导入 STEP 的路径，采集器分别重新打开 SLDPRT 与导出的 STEP，比较实际实体、几何与孔位。两种格式必须各自满足独立期望且彼此一致；长度/hash、合法 STEP 文件头或重复重开同一个原生模型均不足以证明正确导出。

保留的 [中间连续采集失败清单](../output/validation/v2_2_c_cad_baseline/collection-20260914_090727-a3a1077d/manifest.json) 记录第一项四孔模型两格式匹配，但第二项原生三孔模型的 STEP 仍为第一项四孔模型。第二项体积相差 `282.74333882308565` mm³，孔位也不符，采集器正确停止为 `Failed`。该反例推动后续激活及唯一文件名修复，不能删除或追认为通过。

最终比较器检查全量圆柱轴线及孔口位置，并只接受可由目标几何推导出的环面缝；没有放宽容差或忽略未知额外曲面。16 项离线比较器回归、四方向连续采集及七组生产候选验收共同验证该边界。保存前失败若没有可信完整路径，平台不强行关闭未知或未保存文档，该情况仍是需要人工确认的会话清理限制。

## 官方 API 依据与验证边界

`FeatureLinearPattern4` 的第 5 个参数为 `FlipDir1`，用于方向翻转；实际选边与声明方向如何对应，仍由本轮反向边实验验证。[官方线性阵列 API](https://help.solidworks.com/2020/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureLinearPattern4.html)

`FeatureCircularPattern5` 的第 3 个参数为 `FlipDirection`；当 `EqualSpacing=true` 时，`Spacing` 表示以弧度给出的总角度，不能直接把它当作实例间角度。[官方圆周阵列 API](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureCircularPattern5.html)

边没有可直接等同于用户工程意图的天然方向，起点与方向来自其具体边表示。平台必须记录实际边方向，并以相反边和最终落点证明映射正确。[官方边起点 API](https://help.solidworks.com/2020/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IEdge~IGetStartVertex.html)

官方文档支持参数语义，不自动证明当前长参数排列、SolidWorks 版本或选择状态已验证。本轮只沿用已明确的 API；缺证据或测量失败时继续失败关闭。

`SaveAs` 官方合同将 `Errors`、`Warnings` 声明为输出参数；导出 STEP 前须激活目标文档，清空选择以导出完整模型，并同时核对返回值和错误码。项目对同次完整路径的检查是进一步的平台约束。[官方 SaveAs 合同与导出前置条件](https://help.solidworks.com/2017/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.imodeldocextension~saveas.html)

`DispatchWrapper` 强制以 `VT_DISPATCH` 封送对象；Microsoft 对 `IDispatchConstantAttribute` 的说明也区分空 `VT_DISPATCH` 与普通 `null`。这里与本机实际 `TYPEMISMATCH` 及修复后隔离结果共同构成修复依据。[Microsoft DispatchWrapper](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.dispatchwrapper?view=net-10.0)、[Microsoft 空 IDispatch 语义](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.idispatchconstantattribute?view=net-10.0)

通过反射晚绑定调用 COM 时，`ParameterModifier` 用于指定哪些参数按引用传递，本轮据此传回 `Errors` 和 `Warnings`。官方资料说明调用机制，真实 CAD 结论仍以隔离与连续采集证据分别裁决。[Microsoft ParameterModifier](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.parametermodifier?view=net-10.0)

## 方向候选与独立验收方法

新增四份 `examples/v2_2_c_pattern*.json` 用于暴露体积无法识别的方向错误。下表为独立计算的期望值，本次候选实测记录见表后；坐标为重新打开模型后读取的全局 XZ 孔口中心，单位 mm。

| 候选输入 | 验证重点 | 期望孔口落点 |
|---|---|---|
| `v2_2_c_pattern_linear_opposite_edge.json` | 选取方向相反的边，声明 `+X`，4 实例、20 mm 间距 | `(-30,0)`、`(-10,0)`、`(10,0)`、`(30,0)` |
| `v2_2_c_pattern_linear_negative.json` | 声明 `-X`，3 实例、15 mm 间距 | `(30,0)`、`(15,0)`、`(0,0)` |
| `v2_2_c_pattern_circular_positive_quarter.json` | 声明 `+Y`，90° 总角、3 实例 | `(20,0)`、`(√200,-√200)`、`(0,-20)` |
| `v2_2_c_pattern_circular_negative_quarter.json` | 声明 `-Y`，90° 总角、3 实例 | `(20,0)`、`(√200,√200)`、`(0,20)` |

候选输出后关闭并重新打开模型，用只读拓扑探针读取半径 3 mm 的圆边；分别在 `Y=0` 与 `Y=10` 的孔口截面核对数量、位置以及不存在额外孔口。期望位置由几何公式独立计算，不能复制 Adapter 报告中的执行参数当实测值。方向验收与模型物理完整性、STEP 内容、特征结果及体积变化检查同时保留。

9 月 9 日修复后的 [方向候选批次清单](../output/validation/v2_2_c_cad_baseline/collection-20260909_154101-818998b2/manifest.json) 记录 `CandidatesCollected`，4 个项目均为 `CandidateCollected`，模型与 STEP 的物理 hash 在采集及探针复核中保持稳定。文档职责只读核对四份原生模型 probe 后确认两层孔口数量与上述期望坐标一致；正负圆周末端 X 的约 `1.22×10⁻¹⁵` mm 为浮点残差。9 月 14 日新增发现表明这些稳定的 STEP 并非对应模型，故该批记录不能作为两种格式一致性或生产验收依据。

对应证据为 [反向边 +X](../output/validation/v2_2_c_cad_baseline/collection-20260909_154101-818998b2/01-probe.json)、[-X](../output/validation/v2_2_c_cad_baseline/collection-20260909_154101-818998b2/02-probe.json)、[+Y 四分之一圆](../output/validation/v2_2_c_cad_baseline/collection-20260909_154101-818998b2/03-probe.json) 和 [-Y 四分之一圆](../output/validation/v2_2_c_cad_baseline/collection-20260909_154101-818998b2/04-probe.json)。该批次仍记录 `independent_engineering_acceptance=Pending`、`production_bindings_modified=false`；上述坐标核对不擅自改写清单，也不代替生产证据绑定与正式主流程验收。

### 2026-09-14 最终重采与正式交付

最终 [四方向批次](../output/validation/v2_2_c_cad_baseline/collection-20260914_091855-55d13b28/manifest.json) 和 [七组生产批次](../output/validation/v2_2_c_cad_baseline/collection-20260914_092821-db67d733/manifest.json) 在完整导出修复后串行采集，分别重新打开 SLDPRT 和重导入 STEP，比较独立期望、全量轴线、孔口及两格式几何。[七项拓扑验收](../output/validation/v2_2_c_cad_baseline/topology_acceptance_20260914.json) 与 [四项 profile 验收](../output/validation/v2_2_c_cad_baseline/profile_topology_acceptance_20260914.json) 均为 `Passed`。最终报告和九个 Handler、V2.0-D 的对应关系见 [Worker 当前证据绑定](../src/Workers/SolidWorks/api_evidence.md)。

最终 Feature 复合修订为 `feature-execution-source-sha256:ba63ade9e1428efbb7754406d5284879adbf369019494369cef8b32e2e78120a`，V2.0-D 为 `v2.0-d-three-circle-source-sha256:3f20fa75e8d828208d211cfed5f9a46b8c93332ba31cd6238ad5d1437cf928dc`。绑定指向新采集的七份报告，旧 evidence 没有修改或换绑。

[参数更新主流程日志](../output/validation/v2_2_c_cad_baseline/main_workflow_20260914.log) 记录 `Passed` / `Deliverable` / `all_source_reports_passed`，[发布目录](../output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260914_013300_785-0341267380e64d5c8450fbee7c216bd9) 保存本次交付物。[发布件实物复核](../output/validation/v2_2_c_cad_baseline/main_release_geometry_unique_20260914/verification.json) 确认源文件与包内对应 SLDPRT、STEP 的字节和 hash 一致，两种格式均为 200×100×15 mm 板件、四个直径 10 mm 孔，孔口位置正确。

最初按原文件名重开的 [复核失败记录](../output/validation/v2_2_c_cad_baseline/main_release_geometry_20260914) 因会话中的历史同名文档被身份检查拒绝，原样保留。最终复核使用 SHA 相同的唯一名称副本重新打开，既核对真实几何，又避免将历史同名文档误当作发布件；没有修改原交付文件或取消身份约束。

## 执行步骤与最终验收标准

1. 保留修改前测试、旧 evidence 文件集合和 hash；只读检查环境与新模型可读性，不变更保护服务或驱动。
2. 完成种子/图绑定、有符号方向和轴位置约束，再运行对应规则及编译到执行回归。
3. 修复同次完整路径及错误码约束，在最终源码 revision 上串行执行四个方向候选，分别重新打开 SLDPRT 和导入 STEP，独立验收孔口位置与两格式一致性；失败候选原样保留。
4. 重采 7 组生产候选，逐组检查模型与 STEP 的实际长度、hash、可读性、来源 revision、版本及实际几何一致性；不能只对 SLDPRT 使用拓扑探针。
5. 仅在满足现有证据策略后更新当前生产引用，再运行参数更新正式主流程；检查重建、几何、产物、Reviewer、QualityGate 与发布包。
6. 重新运行完整 build、所有测试和定向 self-check，逐一比较原 8 项失败，记录新增失败以及真实 CAD 验收边界。

平台验证命令如下，均不通过测试或 self-check 启动 CAD；真实诊断与主流程的实际命令、输入和运行路径随执行记录回填：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check --output output/validation/v2_2_c_cad_baseline/final_self_check_20260924
```

## 2026-09-14 最终验证记录

| 检查 | 实际结果 | 结论边界 |
|---|---|---|
| 修改前完整测试 | 591 项，583 通过、8 失败 | 基线，不是本阶段完成结果 |
| 最终构建 | 0 警告、0 错误，见 [日志](../output/validation/v2_2_c_cad_baseline/final_build_20260914.log) | 实际源码构建结果 |
| 完整测试 | 684/684 通过，失败 0、跳过 0，见 [日志](../output/validation/v2_2_c_cad_baseline/final_tests_20260914.log) 与 [TRX](../output/validation/v2_2_c_cad_baseline/final_tests/full.trx) | 原 8 项均从 Failed 变为 Passed，见验证汇总；不等于全局自检通过 |
| 执行合同定向回归 | 121/121 通过，见 [日志](../output/validation/v2_2_c_cad_baseline/execution_contracts_owned.log) | 是定向测试总数，不是新增测试数量 |
| 几何比较器回归 | 16/16 通过，见 [记录](../output/validation/v2_2_c_cad_baseline/comparison_regression_20260914.json) | 离线反例覆盖，不代替真机两格式验收 |
| 最终四方向及七组生产重采 | 连续采集及两格式独立验收通过；拓扑 7 项、profile 4 项均 Passed | 仅适用于列明的最终新批次，旧失败仍保留 |
| 生产证据绑定 | 9 个 Handler 与 V2.0-D 指向最终 7 份新报告 | 保留精确 profile、源码与 SolidWorks 版本约束 |
| 参数更新正式主流程与发布件 | Passed / Deliverable，实物复核 Passed | 源与包字节一致，唯一名称副本重开；不将主流程结论泛化到未测能力 |
| 全局 self-check | `final_status=Failed`，见 [报告](../output/validation/v2_2_c_cad_baseline/final_self_check/reports/platform_self_check_report.json) | 夹套真实工作流及生产证据为 false，`shaft_real_workflow_supported=false` 亦参与全局阻断 |
| 恢复相关自检 | Feature、V2.0-D evidence 激活；V2.0-E 受控 plate、STEP 与能力回归门禁为 true，regressions 为空，V2.0-E 最终 Passed | 恢复字段与全局状态分别报告；中文检查通过 |
| 历史文件保留 | 180 个旧保护文件核验 Passed，见 [记录](../output/validation/v2_2_c_cad_baseline/preservation_final_20260914.json) | 不修改旧内容或历史结论 |

此前 38 项规则回归、117 项中间合同回归、环境探针和 COM 隔离日志均为过程记录；最终结果以上表为准，过程记录不删除、不覆盖。

## 2026-09-24 续验记录

本次先确认源代码与证据没有漂移，再补齐中文入口与协议；不重复启动 CAD，也不扩大真实能力范围。

| 检查 | 实际结果 | 证据 |
|---|---|---|
| 历史保留与指纹复核 | Passed；180 个历史保护文件保持不变，所列 235 项指纹检查通过 | [最终保留复核](../output/validation/v2_2_c_cad_baseline/preservation_final_20260924.json) |
| 当前源码复合修订 | 与上述 9 月 14 日 Feature、V2.0-D 修订一致 | [修订复核](../output/validation/v2_2_c_cad_baseline/revisions_resume_20260924.json) |
| 独立证据与发布包复核 | Passed；143 条记录、66 个唯一文件无漂移，发布包 12/12、前后 15/15 指纹匹配 | [质量复核](../output/validation/v2_2_c_cad_baseline/quality_gate_resume_20260924.json) |
| 完整构建 | 退出码 0，0 警告、0 错误 | [构建日志](../output/validation/v2_2_c_cad_baseline/final_build_20260924.log) |
| 完整测试 | 退出码 0，684/684 通过，0 失败、0 跳过 | [测试日志](../output/validation/v2_2_c_cad_baseline/final_tests_20260924.log)、[TRX](../output/validation/v2_2_c_cad_baseline/final_tests_20260924/full.trx) |
| 定向全局自检 | 退出码 2，Failed；轴类与夹套既有缺口保留，恢复证据门禁与中文检查通过，能力回归为空 | [自检报告](../output/validation/v2_2_c_cad_baseline/final_self_check_20260924/reports/platform_self_check_report.json)、[日志](../output/validation/v2_2_c_cad_baseline/final_self_check_20260924.log) |
| 本次验证汇总 | 原 8 项失败逐项转为 Passed；684/684 通过，历史文件和真实主流程实物核验保持通过 | [汇总记录](../output/validation/v2_2_c_cad_baseline/verification_summary_20260924.json) |

本次结果均另存于 `output/validation/v2_2_c_cad_baseline/`，不覆盖 9 月 14 日记录。本次测试生成的四份夹具报告另行归档；确认差异仅为 `generated_at` 后，将受版本管理的原夹具恢复为阶段开始前内容，见 [夹具保留记录](../output/validation/v2_2_c_cad_baseline/test_report_preservation_20260924.json)。真实 CAD 几何证据采集日期仍为 9 月 14 日，9 月 24 日复核文件与来源未漂移，不能混淆两次验证范围。

## 常见失败、修复路径与禁止事项

模型与报告长度或 hash 不符时，保留源文件与失败，核查可信来源或重新采集同次产物；禁止只改 `size_bytes`。引用缺失先检查编译到执行的映射和直接种子 operation，不得放宽单种子前置校验。体积正确但落点不符时检查实际边方向、`Flip`、总角度与轴位置，不能以质量门禁人工审批覆盖几何拒绝。

STEP 可读且字节稳定但几何不符时，先核对实际导出文档身份和完整路径，再分别测量原生模型与重新导入的 STEP；不能将同一份旧 STEP 的稳定 hash 当作本次成功。任一格式几何不匹配都保持失败，不允许只更新报告、复用旧交换文件或省略重导入检查。

源码 revision 变化后重新采集，不直接给旧 evidence 换绑；保护组件运行只作为环境观察记录，不把历史原因未明说成已查清。禁止修改 `reviewrep`、旧报告和历史证据内容，禁止降低能力基线、删除失败测试、并发执行 SolidWorks COM、把 `CandidatePassed` 写成 `Deliverable`，或以本轮恢复为由扩展尚未取证的显式孔与装配能力。
