# SolidWorks Worker 说明

本目录是 SolidWorks 执行层边界。V0.9-B 已提供 `FakeSolidWorksWorker` dry-run skeleton；V1.0 到 V1.7 建立了 `plate_basic_4holes` 的受控真实建模、工程图、质量门禁和发布包链路；V1.8 开始以 `PartTypeRegistry` 与独立 `PartFamilyBuilder` 扩展参数化零件族。

当前仍然默认禁止真实 CAD 执行：

- 本地交互式主流程默认启动或连接 SolidWorks；self-check、CI、单元测试和 dry-run 不启动。
- 默认不调用 COM。
- 默认不调用 `SldWorks.Application`。
- 默认不生成真实 `.SLDPRT`、`.SLDDRW`、`.STEP` 或 PDF 文件。
- 默认不执行真实建模命令。
- 不得复制外部 `solidworks-automation-skill/scripts` 源码。
- 不得把 Python COM 脚本直接塞进 Worker。

## FakeSolidWorksWorker

`FakeSolidWorksWorker` 只生成模拟产物，用于验证平台调度、产物记录、Validator、Reviewer、QualityGate 和 self-check：

- `output/solidworks/artifacts/fake_plate_basic_4holes.SLDPRT.txt`
- `output/solidworks/artifacts/fake_plate_basic_4holes.STEP.txt`
- `output/solidworks/reports/build_report.json`
- `output/solidworks/logs/fake_solidworks_worker.log`

这些文件不是 SolidWorks 原生模型，也不是 STEP 文件，只是 dry-run 产物。

## RealSolidWorksWorker

V1.0-A 的 `RealSolidWorksWorker` 只实现真实执行前边界和连接 smoke test。V1.0-B 在该边界内增加 `RealBuildPlateBasic4Holes`，只支持创建一个 160 x 80 x 12 mm 板件、四个直径 10 mm 通孔，并输出 `.SLDPRT`、`.STEP` 与 `build_report.json`。

真实连接或真实建模由统一运行时策略决定：

- `SolidWorksWorkerRequest.DryRun = false`
- 未设置 `SW_DISABLE_REAL_EXECUTION=true`，且不是 CI、单元测试或强制 Fake Worker

旧字段 `SolidWorksWorkerRequest.AllowRealCadExecution` 仅为反序列化兼容保留，不参与执行授权。

如果任一条件不满足，Worker 必须拒绝真实执行并返回结构化 issue：

- `real_cad_execution_not_enabled`
- `dry_run_mode_enabled`
- `missing_user_safety_confirmation`

当前允许以下执行模式：

- `Fake`
- `RealPreflightOnly`
- `RealConnectionSmokeTest`
- `RealBuildPlateBasic4Holes`
- `RealDrawingBasicViews`

通用 `RealBuild` 仍然是预留模式，不在 V1.0-B 实现。连接 smoke test 成功时只能设置 `RealCadConnected = true`，不能设置 `RealCadExecuted = true`。只有 `RealBuildPlateBasic4Holes` 完成真实建模、保存和导出后，才允许设置 `RealCadExecuted = true`。

`RealDrawingBasicViews` 只在 V1.1 工程图 smoke test 中使用，必须基于已有的 `plate_basic_4holes.SLDPRT` 创建工程图，不得创建尺寸、标题栏、BOM 或装配体工程图。

## V1.0-B 最小真实建模

`RealBuildPlateBasic4Holes` 的目标是验证第一条受控真实 CAD 链路，不是通用建模引擎。当前限制如下：

- 只接受 `BuildPlan.PartType = plate_basic_4holes`。
- 板件尺寸为 160 x 80 x 12 mm。
- 四个通孔直径为 10 mm。
- 输出目录默认在 `output/solidworks/real/plate_basic_4holes/`，如果已有文件则创建带时间戳的子目录。
- 本地交互式无需启用变量；需要关闭时设置 `SW_DISABLE_REAL_EXECUTION=true`。
- 必须提供有效的 `SW_TEMPLATE_PART_PATH`，否则在连接 SolidWorks 前拒绝真实构建。
- self-check 中真实建模 smoke test 还必须显式设置 `SW_REAL_BUILD_SMOKE_TEST=true`。
- 严格模式需要额外设置 `SW_STRICT_REAL_BUILD_TEST=true`。
- 当前不支持工程图、装配体、通用零件建模、批量建模或自然语言到任意模型。

真实建模失败时必须返回结构化 issue，并保留 `build_report.json` 或错误日志，不能把失败伪装成成功。

## V1.1 基础工程图

V1.1 的目标是最小真实工程图链路：

- 打开 `plate_basic_4holes.SLDPRT`。
- 创建 Drawing 文档。
- 插入 Front、Top、Right、Isometric 四个基础视图。
- 保存 `plate_basic_4holes.SLDDRW`。
- 导出 `plate_basic_4holes.pdf`。
- 写出 `drawing_report.json`。

self-check 不执行真实工程图。独立工程图 diagnostic 使用专用 Runner，不能替代 CLI 主流程验收。

手动诊断命令：

```powershell
dotnet run --project tools/SolidWorksDrawingSmokeRunner -- --source-part "output/solidworks/real/plate_basic_4holes/<timestamp>/plate_basic_4holes.SLDPRT" --output output/solidworks/real/plate_basic_4holes_drawing
```

如果需要指定工程图模板，可以使用 `--template` 参数或设置 `SW_TEMPLATE_DRAWING_PATH`。V1.1 不做尺寸标注、标题栏字段、BOM、装配体工程图、复杂模板和钣金展开图。

## SolidWorksSessionManager

`SolidWorksSessionManager` 使用 late binding 封装 `SldWorks.Application` 连接，不引入 SolidWorks Interop 编译期依赖。它负责：

- 按 `SW_VISIBLE` 控制可见模式。
- 按 `SW_CONNECT_TIMEOUT_SECONDS` 控制连接超时。
- 捕获 COM、超时和连接异常并转为结构化 issue。
- 在 `DisconnectAsync` 中释放 COM 对象。
- 不在构造函数中连接 SolidWorks。

self-check 不会调用 `SolidWorksSessionManager.ConnectAsync`。真实连接验证使用独立 Runner 或本地交互式 CLI 主流程。

真实建模 diagnostic 与连接 diagnostic 分离；它们不在 self-check 内运行，也不能替代 `RealBuildPlateBasic4Holes` 的主流程证据。

真实工程图 diagnostic 与真实建模 diagnostic 分离；它们不在 self-check 内运行，最终验收必须经过 QualityGate。

## 平台边界

- Agent 只能提出计划和协作建议，不能直接调用 Worker。
- Gateway 不能直接调用 Worker。
- LLM 不能直接调用 Worker。
- Worker 必须通过平台调度边界执行，并保留审计日志。
- 真实 CAD 执行必须经过请求级开关、环境变量级开关和 QualityGate。
- V1.0-B 的真实建模能力和 V1.1 的真实工程图能力都不改变 Agent、Gateway、LLM 的权限模型。

## V1.8 参数化零件族

### 目标与适用范围

V1.8 通过 `PartTypeRegistry` 解析 `IPartFamilyDefinition`，再由 `PartFamilyBuilderRegistry` 解析对应 `IPartFamilyBuilder`，支持 `plate_basic_4holes`、`flange_basic`、`shaft_basic`。公共 Worker 只负责安全开关、调度、报告和产物边界，不承载大型 `switch(part_type)` 或每族几何细节。

### 输入与输出

输入为已经通过 `CADModelSpecValidator` 与零件族 Validator 的 BuildPlan。输出为 dry-run 或受控真实产物、`build_report.json`、`failure_stage`、ArtifactValidator 结果及后续 Drawing / ReleasePackage 所需的显式路径。

| 零件族 | 参数边界 | V1.8 执行承诺 |
|---|---|---|
| `plate_basic_4holes` | 长、宽、厚、四孔数量、孔径与孔位特征 | 真实能力不回退，必须通过现有回归。 |
| `flange_basic` | 外径、内径、厚度、螺栓孔数、螺栓孔径、分布圆径 | dry-run 必须通过；真实路径必须先有独立 flange smoke。 |
| `shaft_basic` | 直径、长度、可选台阶直径列表与长度列表 | dry-run 必须通过；真实路径必须先有旋转专用诊断证据。 |

### 执行步骤与验证标准

Worker 只接收已注册且参数合法的零件族计划。`unsupported_part_type`、`missing_required_parameter`、`invalid_parameter_value` 必须在 Worker 之前返回。默认 self-check 验证注册、dry-run、失败语义和无大型 switch，不连接 COM 也不启动 SolidWorks。

法兰真实路径可考虑 `CreateCircle`、`FeatureExtrusion2`、`FeatureCut4`，但首次进入主 Worker 前必须独立 smoke。轴的台阶轮廓需要 `CreateLine`、`CreateCenterLine`、`FeatureRevolve2` 的专用 Runner 证据。证据不足时保持 dry-run-only。

### 常见失败与禁止事项

零件族定义、构建计划、构建器、法兰、轴和产物失败分别使用下列阶段：

```text
part_family_definition_missing
build_plan_generation_failed
part_family_builder_missing
flange_build_failed
shaft_build_failed
artifact_validation_failed
```

禁止默认启动 SolidWorks，禁止跳过总调度、工作流程、路由、校验、复审和质量门禁，禁止本轮扩展装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量队列或 V1.9。

## V1.9 Phase 1 真实 build-only Worker

### 目标与输入输出

V1.9 Phase 1 为 `flange_basic` 和 `shaft_basic` 提供真实 Builder 契约，并由 `RealSolidWorksWorker` 统一处理 `PartFamilyBuildContext`、返回 `PartFamilyBuildResult`、保存、STEP 导出和报告。输出执行模式分别为 `RealBuildFlangeBasic` 和 `RealBuildShaftBasic`；plate 仍使用 `RealBuildPlateBasic4Holes` 并保持完整工程图包回归。

`flange_basic` 和 `shaft_basic` 只输出零件、STEP、构建/端到端报告和 build-only 发布包，不自动调用工程图 Builder。

### 执行步骤

Worker 只能从 Gateway / `chief-engineer` 经总调度、工作流程、Router 和两个 Registry 进入。真实调用由 V2.0 统一运行策略决定，并在全局锁下串行执行。

成功产物经 ArtifactValidator、Reviewer 和 QualityGate 后，写入 `output/solidworks/e2e/<part_type>/<timestamp>/`。发布包必须包含两个产物、两份报告和 manifest。

### 验证、失败和禁止事项

self-check 只验证 Builder、主工作流程、V2.0 默认策略、API evidence、ArtifactValidator、plate 回归和 Registry 结构，不连接 COM。失败必须使用专用阶段，不得只返回泛化 `flange_build_failed` / `shaft_build_failed`。

V1.9 Phase 2 已完成两族独立 diagnostic、视觉复核和 CLI 真实主流程回填。flange 与 shaft 均为 `Passed`、`Deliverable`、QualityGate `Passed`，最终报告路径见 `execution.md` 和 `api_evidence.md`。禁止 Builder / SmokeRunner 直接充当最终验收，禁止 flange / shaft 自动工程图，禁止进入 V2.0。
