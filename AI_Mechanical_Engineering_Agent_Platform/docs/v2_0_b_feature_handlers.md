# V2.0-B 通用 Feature Handler

## 阶段结论

V2.0-B 建立从通用 `FeatureGraph` 到 SolidWorks 特征执行边界的注册、参数校验、BuildPlan 适配和 API 证据门禁。默认注册 `sketch`、`extrude_boss`、`extrude_cut`、`hole`、`revolve_boss` 五类 Handler。

本阶段不是通用真实 CAD 验收。五类 Handler 的 `api_evidence_status` 全部为 `unverified`，所以通用 FeatureGraph 的真实执行必须在连接 COM 前返回 `feature_api_evidence_insufficient`。V1.9 的 plate、flange、shaft 专用 Builder 证据仍有效，但它们只覆盖固定零件族和固定参数轮廓，不能自动授权通用 Handler。

## 适用范围

输入是服务端确认需要走 Handler 管线的 `SolidWorksBuildPlan`。输出是注册解析结果、逐操作参数校验结果、全图证据预检结果，以及在未来证据验证完成后由对应 Handler 产生的执行报告。

本阶段包含：

- `IFeatureHandler` 与统一 Handler 元数据。
- 大小写不敏感、拒绝重复注册的 `FeatureHandlerRegistry`。
- 五类 Handler 的参数 Schema、校验、BuildPlan 和报告边界。
- BuildPlan operation 到 Handler feature type 的适配。
- 整图一次性证据预检和 COM 前阻断。
- 每类 Handler 的 `api_evidence.md` 与 `failure_repair.md`。

本阶段不包含装配体、BOM、批量任务队列、第三方脚本生产路径，也不把候选 API 或历史零件族结果写成通用真实授权。

## 统一 Handler 合约

每个 `IFeatureHandler` 必须提供：

- `FeatureType`：Registry 使用的稳定特征类型。
- `HandlerId` 与 `HandlerVersion`：证据和报告的可追溯标识；当前版本为 `2.0-b.1`。
- `FailureStage`：该 Handler 的默认失败阶段。
- `ParameterSchema`：必需、可选参数和单位。
- `ApiEvidence`：API、参数、返回值、前置条件、已知失败模式和验证状态。
- `CanHandle`、`Validate`、`BuildPlan`：无 COM 的解析、校验与计划能力。
- `ExecuteAsync`、`GenerateReport`：受证据门禁保护的执行与报告能力。

`FeatureApiEvidence.AllowsRealExecution` 只有在状态为 `verified` 时才为真。当前五类 Handler 的 `ExecuteAsync` 仍保留第二道锁：即使调用方错误跳过预检，也返回 `feature_api_evidence_insufficient`。

## Registry 与适配规则

`FeatureHandlerRegistry.CreateDefault()` 注册 `SketchHandler`、`ExtrudeBossHandler`、`ExtrudeCutHandler`、`HoleHandler` 和 `RevolveBossHandler` 五类实现。查找忽略大小写，重复类型注册直接拒绝，未知类型返回 `unsupported_feature_type`。新增 Handler 通过注册扩展，不要求修改 `RealSolidWorksWorker`，也不允许增加大型 `switch(feature_type)`。

当前 BuildPlan operation 适配如下：

| BuildPlan operation | Handler `FeatureType` |
|---|---|
| `CreateSketch`、`CreateCenterLine` | `sketch` |
| `ExtrudeBoss` | `extrude_boss` |
| `CutExtrude` | `extrude_cut` |
| `AddHoleWizardHole` | `hole` |
| `RevolveBoss` | `revolve_boss` |

`SavePart` 与 `ExportStep` 不属于几何 Handler，由既有保存和导出边界处理。任何其他几何 operation 都必须以 `unsupported_feature_type` 停止，不能静默忽略。

## 服务端执行来源

是否进入 Handler 管线由服务端编译过程写入，不接受请求 JSON 自行提升权限。调用方提供草图或特征并经过通用编译时，计划标记为 `feature_handler_graph`；固定零件族生成图仍保留 `part_family_builder`，继续走 V1.9 专用 Builder。

该边界同时保证：

1. 外部输入不能伪造 Handler 执行授权。
2. V1.9 plate、flange、shaft 的真实能力不会因 V2.0-B 的通用证据未验证而回退。
3. 任意通用 FeatureGraph 不会借用固定零件族证据进入真实 COM。

## 全图预检与真实执行边界

真实 Worker 的顺序必须是：

```text
SolidWorksBuildPlan
→ 运行环境安全检查
→ 判断服务端执行来源
→ 适配全部几何 operation
→ Registry 解析全部 Handler
→ 校验全部参数
→ 检查全部 Handler 的 API evidence
→ 全部 verified 后才允许连接 COM
→ SolidWorksFeatureGraphPartFamilyBuilder
→ ArtifactValidator
→ Reviewer
→ QualityGate
```

当前任一操作命中未验证 Handler 时，整张图返回 `feature_api_evidence_insufficient`，并保持 `RealCadConnected=false`、`RealCadExecuted=false`、COM 连接计数为零。系统不得先执行已验证子图，再在未验证节点处留下部分模型。

## 五类 Handler 状态

| Handler | 最小参数规则 | 候选 API 或策略 | `api_evidence_status` | 当前结论 |
|---|---|---|---|---|
| `sketch` | 必需 `reference_plane`、非空 `entities`；可选 `constraints`、`dimensions` | `ISketchManager.InsertSketch`、`CreateLine`、`CreateCircle`、`CreateCenterRectangle` | `unverified` | V1.9 只覆盖固定基准和固定实体子集；通用引用、闭合性及更多实体未验证。 |
| `extrude_boss` | 必需正数 `depth_mm`；`direction` 仅允许 `blind` 或 `mid_plane` | `IFeatureManager.FeatureExtrusion2` | `unverified` | V1.9 固定盲拉伸不证明通用参数映射，`mid_plane` 也未诊断。 |
| `extrude_cut` | `through_all=true` 或正数 `depth_mm` 至少满足一个 | `IFeatureManager.FeatureCut4` | `unverified` | V1.9 使用固定盲切超深参数，不证明通用 `through_all` 映射。 |
| `hole` | `hole_diameter_mm` 或 `diameter_mm` 至少一个为正数 | 暂无接受的 Hole Wizard 映射 | `unverified` | V1.9 采用草图圆加切除，并未验证通用 Hole Handler；`HoleWizard5` 仅是被拒绝的研究候选。 |
| `revolve_boss` | `angle_degrees` 大于 0 且不超过 360；可选 profile mark `0`、axis mark `16` | `FeatureRevolve2`、`ISelectData.Mark`、`IEntity.Select4` | `unverified` | V1.9 只验证固定 360° 轴族路径，没有通用引用解析和参数轮廓证据。 |

每类详细证据和修复步骤位于 `src/Workers/SolidWorks/Features/<Handler>/`。证据提升到 `verified` 前，至少要记录 `EvidenceId`、`HandlerVersion`、准确参数轮廓、SolidWorks 版本、诊断路径、源码修订、API 返回值、重建结果和几何产物验证。

## 失败阶段

| `failure_stage` | 含义 | 处理原则 |
|---|---|---|
| `unsupported_feature_type` | operation 无适配或 Registry 未注册 | 补适配、实现、注册、测试和文档；不得默认映射为其他特征。 |
| `invalid_feature_parameter` | Handler 参数缺失、类型错误、数值或枚举非法 | 在无 COM 校验中修正输入；不得由执行层猜测默认值。 |
| `sketch_reference_missing` | 草图目标基准或引用为空 | 修正基准引用并重跑图校验。 |
| `unsupported_sketch_entity` | 草图实体不在 Handler 已声明集合中 | 延期该实体或补完整实现与证据。 |
| `feature_api_evidence_insufficient` | 至少一个 Handler 的证据不是 `verified` | 停止真实执行，先完成独立诊断和证据审查。 |

发生上述任一失败时，不得连接 SolidWorks，不得回退 Fake Worker 冒充真实成功，也不得绕过 ArtifactValidator、Reviewer 或 QualityGate。

## 验证与 self-check

V2.0-B 要求以下字段全部通过：

```text
feature_handler_registry_exists
no_feature_type_large_switch
sketch_handler_registered
extrude_handler_registered
cut_handler_registered
hole_handler_registered
revolve_handler_registered
feature_handler_validation_supported
feature_api_evidence_required
unverified_api_blocks_real_execution
feature_handler_docs_completed
v2_0_b_documented
```

验证还必须覆盖大小写不敏感查找、重复注册拒绝、未知类型拒绝、非法参数拒绝、无需修改 Worker 即可注册新 Handler、全图证据预检、未验证证据在 COM 前阻断，以及 V1.9 零件族计划继续使用 `part_family_builder`。

## 禁止事项

- 禁止增加大型 `switch(feature_type)`。
- 禁止把 `unverified`、候选 API、历史文件存在或 V1.9 专用诊断解释为通用真实授权。
- 禁止在参数校验或证据预检失败后连接 COM。
- 禁止复制第三方代码或把第三方脚本接入生产路径。
- 禁止从请求 JSON 伪造 `feature_handler_graph` 执行来源。
- 禁止借本阶段进入装配体、BOM、队列或后续阶段。
