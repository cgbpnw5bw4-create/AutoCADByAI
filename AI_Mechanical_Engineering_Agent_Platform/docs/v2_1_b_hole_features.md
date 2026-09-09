# V2.1-B 孔特征增强

## 目标

让通用 FeatureGraph 正确表达并验证普通孔、沉孔、沉头孔和标准攻丝孔，统一使用 `HoleFeatureDefinition` → `HoleHandler` → `HoleValidator` → `ISolidWorksFeatureAdapter` → `GeometryValidator` → `QualityGate`。参数与图引用校验在 Worker 前执行，后置几何检查独立于 COM 成功返回。

## 准入与适用范围

前置复杂特征库审查为 [2026-09-07 Claude 报告](../../reviewrep/2026-09-07-v2.1-a-complex-feature-library-review.md)，被审源码 `HEAD=bbeafc9`，结论 `PASS WITH COMMENTS`，没有 `Blockers`。报告独立性受限，作者参与被审实现；全部改进建议登记 [技术债](technical_debt.md)，按用户要求不阻塞本阶段且不顺带实施。

本阶段只扩展既有 `hole` Feature，复用唯一 `HoleHandler`、Registry、Worker 和 Adapter。没有新增零件族或孔专用 Agent，不改变 FeatureHandler 架构，不进入 V2.1-C。

## 输入与输出

输入为单位 `mm` 的 `CADModelSpec`、FeatureGraph 及 `FeatureDefinition.Parameters` 中的孔参数。标准化结果为 `HoleFeatureDefinition`，编译为 `CreateHole` 操作并通过现有 `ExecuteHoleAsync` 统一入口分发。历史不带 `hole_type` 的 `CreateSimpleHole` 单圆盲切仅保留其原档案。

输出包括校验结果、标准化 BuildPlan、记录实际 `hole_type` 与 `hole_parameters` 的 Feature 报告、孔几何检查和自检结果。模拟入口输出 `output/solidworks/dry-run/<runId>/dry_run_report.json`，记录 `simulation_status`、`quality_gate`、`issues` 和关联产物；无论模拟是否通过，均为 `dry_run=true`、`real_cad_executed=false`、`deliverable_status=NotDeliverable`。

## 类型与参数合同

| 类别 | 参数 | 校验与语义 |
|---|---|---|
| 通用 | `hole_type` | `SimpleHole`、`CounterboreHole`、`CountersinkHole`、`TappedHole`；大小写规范化，未知类型返回 `unsupported_hole_type` |
| 通用 | `diameter_mm` | 有限正主孔径；攻丝孔为底孔几何直径，公称径来自 `thread_size` |
| 通用 | `depth_mm`、`through_all` | 盲孔必须有有限正深度并保留底部材料；`through_all=true` 可省略孔深，若提供则仍必须为正；不能用超深盲切伪装贯穿语义 |
| 通用 | `position` | 一个坐标对象或显式坐标数组；使用参考面的局部毫米坐标 `x_mm`、`y_mm`、`z_mm`，`z_mm=0`，所有坐标有限 |
| 通用 | `reference_face` | `<boss_feature_id>:start_face` 或 `<boss_feature_id>:end_face`；对应直接依赖的单一盲拉伸实体 |
| 通用 | `quantity` | `1..1000`，默认 1；必须等于显式孔位数量，拒绝重复、相交或相切孔位 |
| 通用 | `pattern_reference` | 可选 `sketch:<id>` 放置草图引用；必须在图中声明且与基础轮廓处于同一局部坐标平面，其等径圆心点集与 `position`、`quantity` 完全一致；不隐式创建阵列 |
| 沉孔 | `counterbore_diameter_mm`、`counterbore_depth_mm` | 大径严格大于主孔径，台阶深度为正且小于孔深及实体厚度 |
| 沉头孔 | `countersink_diameter_mm`、`countersink_angle_deg` | 入口径大于主孔径，全夹角为 `(0,180)` 度；锥段高度 `(D-d)/(2*tan(angle/2))` 不得穿过孔底或实体 |
| 攻丝孔 | `thread_standard`、`thread_size`、`thread_pitch` | 有限 `ISO_METRIC` 粗牙目录；规格与螺距匹配，不接受未知标准或随意字符串 |
| 攻丝孔 | `thread_depth_mm`、`tap_drill_diameter_mm` | 螺纹有效深度为正且不超过有效孔深/材料厚度；底孔为有限正数、小于公称径且等于 `diameter_mm` |

有限粗牙目录为 `M3×0.5`、`M4×0.7`、`M5×0.8`、`M6×1`、`M8×1.25`、`M10×1.5`、`M12×1.75`、`M16×2`。当前候选底孔白名单依次为 `2.5`、`3.3`、`4.2`、`5`、`6.75`、`8.5`、`10.25`、`14 mm`，超出该表拒绝。此表仅定义项目保守候选范围，不是通用 ISO 工艺规定、完整标准数据库或本机孔向导数据库的已验证映射；不根据经验式推导任意规格。

### 几何范围与引用限制

Worker 前的材料范围证明仅覆盖一个矩形或圆形闭合草图盲拉伸实体，加直接孔特征的图。参考面局部轮廓必须可由图参数确定；孔完整外缘（沉孔/沉头按最大径）须严格处于材料内，不接受边界相切。未知面、复杂布尔、阵列后材料、曲面或多个基础实体无法由当前模型证明时拒绝，不把包围盒当任意实体的有效面。

多孔使用显式位置而非推测阵列；同一实体其他孔的投影相交也保守拒绝。图级预检与实际编译计划重构校验都必须运行，防止修改 BuildPlan 后绕过原始输入校验。该受限通用模型没有零件名称特判。

## Handler、Adapter 与证据边界

`HoleExecutionStrategies` 以类型注册表保存失败阶段和 API evidence；保持现有 `ExecuteHoleAsync`，不创建四个重复 Handler，也不以大型 switch 分散类型逻辑。`GenerateReport` 写出实际孔类型与标准化参数，类型化新档案报告 `unverified`，不得继承历史普通孔 evidence 字段冒充授权。

| 类型 | Handler/标准化请求 | API 状态 | 真实执行 |
|---|---|---|---|
| `SimpleHole` | 由既有 Handler 处理显式类型、盲孔/通孔、孔位与面 | 新档案 `unverified`；历史单圆盲切单独受证 | 新类型化档案拒绝；旧档案须通过当前源码绑定 |
| `CounterboreHole` | 由同一 Handler 保留大径和台阶深度 | 孔向导候选 `unverified` | 连接前拒绝 |
| `CountersinkHole` | 保留入口径与全角 | 孔向导候选 `unverified` | 连接前拒绝 |
| `TappedHole` | 保留标准、规格、螺距、有效深度和底孔 | 标准攻丝孔候选 `unverified` | `tapped_hole_api_unverified`，连接前拒绝 |

候选研究使用 `CreateDefinition(swFmHoleWzd)`、`IWizardHoleFeatureData2.InitializeHole` 与 `CreateFeature`；`HoleWizard5` 作为参数对照资料。官方 API、单位、选择、返回、宏和失败模式见 [孔 API 证据](../src/Workers/SolidWorks/Features/Hole/api_evidence.md)。没有有效证据的类型仅有明确失败关闭入口，不声称已实现真实建孔。

### 攻丝语义

普通圆加 Cut 是几何孔；`Cosmetic Thread` 是螺纹表示；标准 `Hole Wizard / Tapped Hole` 必须有实际孔向导类型与螺纹元数据；真实建模螺纹还需独立螺旋实体特征。四层不能互相替代。本阶段定义与校验可正确表达标准攻丝孔，但真实创建和元数据读回仍待证据，不把最低语义表达要求写成真机成功。

## 几何验证

孔后置检查必须对比独立测量与图中期望，而不能只看 Feature 非空。至少涵盖：

- `hole_count` 与每个实际孔的存在。
- 主孔直径、位置、有效深度与贯穿语义。
- 沉孔的大径、台阶深度及同轴两级几何。
- 沉头孔入口径、锥角及主孔几何。
- 攻丝孔实际 `HoleWzd` 特征、`hole_wizard_tapped` 表示、标准/规格/螺距/深度/底孔元数据。当前候选要求存在装饰螺纹且没有实体建模螺纹；普通切除或仅装饰螺纹不可通过。

`MeasuredGeometry.Holes` 承载 `MeasuredHole` 与 `MeasuredTappedHole` 的独立纯数据，`HoleGeometryValidation` 由现有 `GeometryValidator` 调用。除了总孔数，还按 `FeatureId` 与孔位逐一消费读数，防止用其他特征的孔补数量；同次特征树必须存在且 `ErrorCode=0`，不可用错误码也不能作为成功。期望体积覆盖主孔圆柱、沉孔额外圆柱和沉头锥台，攻丝候选仅计算底孔体积，不凭空加入螺旋切除量。

通用 Worker 的 `ValidateExpectedGeometry` 已接入该判定；构建报告携带 `hole_model_spec` 与 `hole_geometry_validation`。`SolidWorksArtifactValidator` 再次计算孔几何结果，并逐项对照 `feature_handler_reports` 中报告的孔定义；缺少定义、测量或报告关联，或者定义与 Handler 报告不一致，均不得交付。

本轮尚未提供四类新档案的已验证真实逐孔 Reader，生产 Reader 不生成这四类的 `Holes`，不能从请求参数填充“实测”孔数据。`Holes` 缺失或任何属性不匹配时返回 `hole_geometry_validation_failed`，并由同次质量门禁拒绝。自动测试中的独立测量夹具只验证判定规则；已实现的是纯 DTO 判定合同、链路接入与失败保护，真实读取能力仍待证据。

## 本轮既有参数档案真实重采

本轮受绑定源码发生变化后，使用既有诊断 Runner 重新执行七个旧档案。下表每个目录均含 `feature_execution_report.json`、`probe.json`、`probe_console.log`、`model.SLDPRT`、`model.STEP`；并非修改旧报告以复用旧源码哈希。

七份报告均记录 SolidWorks `31.5.0`、`real_cad_executed=true`、`CandidatePassed`、`NotDeliverable`，执行链修订为 `feature-execution-source-sha256:e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09`。根代理另外对照闭式体积与只读探针的孔口位置、半径和高度：体积误差小于 `6e-11 mm³`。圆边数用于拓扑复核，不等于孔数，不能以圆边数量单独判断孔正确。

| 既有档案 | 本轮诊断报告 | 最终实测体积（立方毫米） | 探针圆边数 |
|---|---|---|---|
| 基础草图、凸台、切除与隐式普通孔 | [20260907_013221_3210868](../evidence/solidworks/v2_1_b_refresh/20260907_013221_3210868/feature_execution_report.json) | `58429.2036732051` | `4` |
| 既有等半径圆角 | [20260907_013408_5080299](../evidence/solidworks/v2_1_b_refresh/20260907_013408_5080299/feature_execution_report.json) | `58415.11747102787` | `6` |
| 既有距离与角度倒角 | [20260907_013423_5400787](../evidence/solidworks/v2_1_b_refresh/20260907_013423_5400787/feature_execution_report.json) | `58395.693351566806` | `6` |
| 既有线性阵列 | [20260907_013438_4901315](../evidence/solidworks/v2_1_b_refresh/20260907_013438_4901315/feature_execution_report.json) | `58869.02664470767` | `8` |
| 既有圆周阵列 | [20260907_013451_0073586](../evidence/solidworks/v2_1_b_refresh/20260907_013451_0073586/feature_execution_report.json) | `58083.628481310225` | `10` |
| 既有基准面镜像 | [20260907_013503_7710789](../evidence/solidworks/v2_1_b_refresh/20260907_013503_7710789/feature_execution_report.json) | `59434.513322353836` | `4` |
| 既有参数更新样例的更新后四孔板档案 | [20260907_013514_0906527](../evidence/solidworks/v2_1_b_refresh/20260907_013514_0906527/feature_execution_report.json) | `295287.6110196154` | `8` |

参数更新诊断直接构建已更新的 `200×100×15 mm` 模型，仅证明该更新后档案；它不证明本轮已运行“初始构建 → 修改参数 → 重建”的完整闭环，也不替代 `run-cad-workflow` 的主流程验收。七份诊断均不是可交付包。

这些重采已用于恢复九个既有 Handler 精确参数档案的当前源码绑定；只更新证据标识、诊断路径与源码修订等元数据，未重写历史报告。[独立复核记录](../evidence/solidworks/v2_1_b_refresh/independent_verification.json) 的 `all_passed=true`；V2.0-D 三圆策略另外按原算法重算为 `v2.0-d-three-circle-source-sha256:8869ed288e23378e75683983b62545de1afdaa7e707bca765434dcec38a26ba3`，由 `quality_gate` 独立复核。

新增显式 `SimpleHole`、`CounterboreHole`、`CountersinkHole`、`TappedHole` 及其真实逐孔 Reader 仍为 `unverified`；`through_all`、新面引用、孔向导和实体螺纹不能从本表获得授权。绑定恢复不等于本轮主流程交付，最终回归结论见下方验证记录。

## 执行步骤

1. 读取 AGENTS、阶段索引、能力矩阵、执行协议、模块与 Worker 文档，以及相关技能和审查报告。
2. 校验定义、图引用和有效材料范围，按注册表生成规范请求；非法请求在 Worker 前停止。
3. 默认运行四个 dry-run 样例并覆盖负例；不启动 SolidWorks。
4. 当前源码或精确参数档案缺 evidence 时拒绝真实执行。对既有档案的任何重新采证必须保持诊断与最终主流程验收分离。
5. 运行下列完整验证命令，保存真实退出码和报告；同步矩阵与 API 文档后提交 Claude 审查，不进入 V2.1-C。

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

## 回归样例与验证标准

| 样例 | 主要覆盖 | 默认模式 |
|---|---|---|
| `examples/hole_simple_plate.json` | 显式普通孔定义、位置与参考面 | `dry_run=true` |
| `examples/hole_counterbore_plate.json` | 主孔与大径台阶 | `dry_run=true` |
| `examples/hole_countersink_plate.json` | 主孔与入口锥段 | `dry_run=true` |
| `examples/hole_tapped_plate.json` | 标准攻丝定义、底孔与螺纹元数据 | `dry_run=true` |

### 四类样例的可执行入口

在项目根目录分别执行：

```powershell
dotnet run --project src/Interfaces/CliHost -- dry-run-cad --input examples/hole_simple_plate.json
dotnet run --project src/Interfaces/CliHost -- dry-run-cad --input examples/hole_counterbore_plate.json
dotnet run --project src/Interfaces/CliHost -- dry-run-cad --input examples/hole_countersink_plate.json
dotnet run --project src/Interfaces/CliHost -- dry-run-cad --input examples/hole_tapped_plate.json
```

该入口强制模拟：即使输入写 `dry_run=false` 或环境启用了本地真实执行，也不能覆盖 `dry_run=true`。请求仍经过 `AgentMessageDispatcher` → `chief-engineer` → Workflow 与模块 → FakeWorker → Validator / Reviewer → QualityGate，不直接调用 Builder 或 Adapter。

请求进入工作流后，成功时退出码为 `0`，报告为 `simulation_status=Passed`；校验失败时退出码为 `2` 并保留原因。两种报告均明确 `real_cad_executed=false`、`NotDeliverable`；输入文件或 JSON 无法解析时也返回 `2`，原因写入终端，可能尚未生成运行报告。`run-cad-workflow` 的真实交付门禁保持不变，不能用模拟通过替代真实孔证据或最终主流程验收。

### 自检合同

必需自检字段如下；名称中的 `supported` 仅表示本阶段定义、校验和模拟合同，真实授权另看 evidence 与同次实测：

```text
simple_hole_supported
counterbore_hole_supported
countersink_hole_supported
tapped_hole_supported
hole_type_validation_supported
hole_geometry_validation_supported
tapped_hole_semantics_separated_from_simple_cut
hole_api_evidence_required
unverified_hole_blocks_real_execution
hole_feature_regression_tests_passed
v2_1_b_documented
markdown_chinese_check_passed
```

除四类正例外，必须覆盖未知类型、非有限/非正尺寸、沉孔/沉头非法关系、螺纹目录与深度错误、引用缺失、孔外缘越界、数量/点集不符、跨孔重叠、计划篡改、未验证真实执行和“非空 Feature 但无孔几何”反例。

## 常见失败与禁止事项

类型失败使用 `unsupported_hole_type`，输入失败使用 `invalid_hole_parameter`，引用失败使用 `hole_reference_face_missing`；真实执行各类型有专用失败阶段，攻丝证据不足使用 `tapped_hole_api_unverified`，其余证据不足使用 `feature_api_unverified`；孔后置失配使用 `hole_geometry_validation_failed`。详细修复与关闭条件见 [孔失败修复](../src/Workers/SolidWorks/Features/Hole/failure_repair.md)。

禁止 Handler COM、未验证 API 实调、第三方脚本复制、大型类型 switch、零件专用逻辑、猜测面或数据库枚举、普通 Cut 冒充攻丝、非空 Feature 冒充几何成功、改写旧 evidence 绑定冒充重采、削弱基线或进入 V2.1-C。

## 本轮验证记录

以下结果来自本轮最终日志。孔阶段检查和构建测试已通过，全局自检仍因既有夹套证据缺口为 `Failed`；两者分别记录，不能使用前置审查或中途失败轮次代替最终结果。

| 检查 | 本轮结果 | 证据与限制 |
|---|---|---|
| 完整 `dotnet build` | 通过，退出码 `0`，`0` 错误、`0` 警告 | [最终构建日志](../output/tests/v21_b/build_final.log) |
| 完整 `dotnet test` | `449/449` 通过，`0` 失败、`0` 跳过，退出码 `0` | [最终测试日志](../output/tests/v21_b/test_final.log)、[最终 TRX](../output/tests/v21_b/v21_b_full.trx) |
| 孔专项自动测试 | `63/63` 通过，`0` 失败、`0` 跳过 | [专项日志](../output/tests/v21_b/targeted_frozen.log)、[专项 TRX](../output/tests/v21_b/v21_b_targeted.trx) |
| 四类 `dry-run-cad` 与矛盾输入 | 五次均模拟 `Passed`，退出码 `0` | 四类样例及 `dry_run=false` 与真实环境开启组合均强制模拟，`real_cad_executed=false`、`NotDeliverable`；具体日志见下 |
| V2.1-B 十二个必需字段 | 十一个孔阶段字段与 `markdown_chinese_check_passed` 全部为 `true` | [本轮自检报告](../output/reports/platform_self_check_report.json)；中文检查 `83/83`，`0` 失败、`0` 警告 |
| 全局 self-check | `Failed`，退出码 `2` | [最终自检日志](../output/tests/v21_b/self_check_final.log)；既有 `jacket_real_workflow_supported=false`、`jacket_production_evidence_active=false`，不得声称全局通过 |
| 历史普通孔与既有 Feature profile | 七份重采均 `CandidatePassed`，九个既有 Handler 及三圆策略绑定已复核恢复 | 见重采表和独立复核记录；保持 `NotDeliverable`，不扩大新档案 |
| 新四类显式孔真机验证 | `unverified` | 没有实际新四类 Reader、创建或螺纹读回证据；全部阻止真实执行 |
| Claude 审查准入 | 可以进入独立 Claude 实现审查 | 必须披露全局自检失败、四类显式孔真实未验证及前置审查独立性限制；不代表新增真实孔可用，不进入 V2.1-C |

模拟入口日志：[普通孔](../output/tests/v21_b/cli_simple_final.log)、[沉孔](../output/tests/v21_b/cli_counterbore_final.log)、[沉头孔](../output/tests/v21_b/cli_countersink_final.log)、[攻丝孔](../output/tests/v21_b/cli_tapped_final.log)、[矛盾输入](../output/tests/v21_b/cli_contradictory_final.log)。每份日志记录各自 `dry_run_report.json` 的精确路径；五次输出不能充当真实 CAD 交付证据。

重采前因旧源码绑定失效而失败的中途检查保留于 `output/tests/v21_b/full_test_before_evidence.log`、`test_before_three_circle_rebind.log` 与 `v21_b_before_three_circle_rebind.trx`。相应问题经本轮真实重采与独立绑定复核解决；最终测试以 `test_final.log` / `v21_b_full.trx` 的 `449/449` 为准，没有删除失败日志或降低门禁来制造通过。

最终自检同时确认 `feature_production_evidence_active=true`、`v2_0_d_production_evidence_active=true`、`v2_0_e_final_status=Passed`、`v2_0_e_capability_regressions=[]`。夹套既有证据缺口继续失败关闭，本轮未修复或豁免该门禁；新四孔型的 `unverified` 也没有因旧档案重采、自动测试或中文检查通过而提升。
