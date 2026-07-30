# SolidWorks Worker 执行说明

## 组件职责

- `FakeSolidWorksWorker`：默认 dry-run Worker，只生成模拟 artifact，不启动 SolidWorks。
- `RealSolidWorksWorker`：真实执行入口，受安全开关保护。
- `SolidWorksSessionManager`：封装 COM 连接、超时和释放。
- `SolidWorksPlaneSelector`：兼容中英文模板的基准面选择。
- `SolidWorksPlateFeatureBuilder`：封装板件建模 API，不让主 Worker 堆满 dynamic COM 调用。
- `SolidWorksDrawingBuilder`：封装 V1.1 工程图基础视图 API，不让 `RealSolidWorksWorker` 直接堆满工程图 COM 调用。
- `SolidWorksDrawingDimensionBuilder`：封装 V1.2 工程图基础尺寸 API，只在已有工程图上增加最小尺寸标注。
- `SolidWorksDrawingTitleBlockBuilder`：封装 V1.3 工程图标题栏基础信息 API，只在带尺寸工程图上写入最小自定义属性并刷新工程图。
- `SolidWorksReleasePackageBuilder`：封装 V1.4 发布包收集逻辑，只复制已有真实输出并生成 manifest、summary 和 quality report，不启动 SolidWorks。
- `SolidWorksSmokeRunner`：独立诊断 Runner，不依赖 Agent、Gateway、LLM 或 WorkflowEngine。
- `SolidWorksDrawingSmokeRunner`：V1.1 工程图独立诊断 Runner，只能手动运行，不被默认 self-check 调用。
- `SolidWorksDrawingDimensionSmokeRunner`：V1.2 工程图尺寸独立诊断 Runner，只能手动运行，不被默认 self-check 调用。
- `SolidWorksDrawingTitleBlockSmokeRunner`：V1.3 工程图标题栏独立诊断 Runner，只能手动运行，不被默认 self-check 调用。
- `SolidWorksEnvironmentValidator`：执行前预检系统、模板、输出目录和安全开关。

## 执行顺序

本地交互式主流程默认走 `RealSolidWorksWorker`。真实执行先通过 `SolidWorksEnvironmentValidator`，再由 `SolidWorksExecutionEnvironmentProbe` 只读确认 Windows、交互式桌面和 `SldWorks.Application` 注册，然后由 `SolidWorksSessionManager` 连接，最后由对应 PartFamilyBuilder 执行受控建模步骤；dry-run、CI、单元测试和显式禁用走 Fake Worker。环境探测失败必须以 `real_execution_environment_unavailable` 结束，不能连接 COM，也不能自动回退 Fake Worker。

## V1.1 工程图基础视图链路

V1.1 只允许基于已经生成的 `plate_basic_4holes.SLDPRT` 创建基础工程图。最小链路如下：

```text
plate_basic_4holes.SLDPRT
→ SolidWorksDrawingBuilder
→ 新建 Drawing 文档
→ 插入 Front / Top / Right / Isometric 基础视图
→ 保存 plate_basic_4holes.SLDDRW
→ 导出 plate_basic_4holes.pdf
→ 写出 drawing_report.json
→ SolidWorksArtifactValidator
```

本地交互式真实工程图主流程遵循 V2.0 默认启用策略；self-check 不进入工程图 diagnostic，独立 Runner 不能作为最终验收。

独立诊断命令：

```powershell
dotnet run --project tools/SolidWorksDrawingSmokeRunner -- --source-part "output/solidworks/real/plate_basic_4holes/<timestamp>/plate_basic_4holes.SLDPRT" --output output/solidworks/real/plate_basic_4holes_drawing
```

V1.1 不做尺寸标注、标题栏、BOM、装配体工程图、复杂模板和钣金展开图。

## V1.2 工程图基础尺寸链路

V1.2 只允许在 V1.1 已生成的 `plate_basic_4holes.SLDDRW` 上增加最小基础尺寸。最小链路如下：

```text
plate_basic_4holes.SLDDRW
→ SolidWorksDrawingDimensionBuilder
→ 确认 Front / Top / Right / Isometric 视图
→ 添加 160 mm 长度、80 mm 宽度、12 mm 厚度、Φ10 孔径、120 mm 与 40 mm 孔中心距
→ 保存 plate_basic_4holes_dimensioned.SLDDRW
→ 导出 plate_basic_4holes_dimensioned.pdf
→ 写出 dimension_report.json
→ SolidWorksArtifactValidator
```

本地交互式真实尺寸主流程遵循 V2.0 默认启用策略；self-check 不进入尺寸 diagnostic，独立 Runner 不能作为最终验收。

独立诊断命令：

```powershell
dotnet run --project tools/SolidWorksDrawingDimensionSmokeRunner -- --source-drawing "output/solidworks/real/plate_basic_4holes_drawing/<timestamp>/plate_basic_4holes.SLDDRW" --output output/solidworks/real/plate_basic_4holes_drawing_dimensions
```

V1.2 不做 BOM、标题栏、国标模板美化、自动全尺寸标注、复杂公差、表面粗糙度、装配图、钣金展开图或 V1.3 内容。

## V1.3 工程图标题栏基础信息链路

V1.3 只允许在 V1.2 已生成的 `plate_basic_4holes_dimensioned.SLDDRW` 上写入最小标题栏/图纸属性信息。最小链路如下：

```text
plate_basic_4holes_dimensioned.SLDDRW
→ SolidWorksDrawingTitleBlockBuilder
→ 读取当前 Sheet 和比例
→ 通过 CustomPropertyManager 写入 PartName、DrawingNumber、Material、Scale、DrawingDate、Revision
→ 刷新标题栏字段引用
→ 保存 plate_basic_4holes_title_block.SLDDRW
→ 导出 plate_basic_4holes_title_block.pdf
→ 写出 title_block_report.json
→ SolidWorksArtifactValidator
```

本地交互式真实标题栏主流程遵循 V2.0 默认启用策略；self-check 不进入标题栏 diagnostic，独立 Runner 不能作为最终验收。

独立诊断命令：

```powershell
dotnet run --project tools/SolidWorksDrawingTitleBlockSmokeRunner -- --source-drawing "output/solidworks/real/plate_basic_4holes_drawing_dimensions/<timestamp>/plate_basic_4holes_dimensioned.SLDDRW" --output output/solidworks/real/plate_basic_4holes_title_block
```

V1.3 不做 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4 内容。标题栏字段采用文档级自定义属性驱动；若模板未引用这些属性，报告仍必须记录属性写入状态和 `failure_stage`。

## V1.4 工程发布包与质量检查链路

V1.4 只整理 V1.0-B、V1.1、V1.2 和 V1.3 已经生成的真实输出，不创建或修改 CAD 文件。最小链路如下：

```text
output/solidworks/real/plate_basic_4holes/**
output/solidworks/real/plate_basic_4holes_drawing/**
output/solidworks/real/plate_basic_4holes_drawing_dimensions/**
output/solidworks/real/plate_basic_4holes_title_block/**
output/solidworks/diagnostics/plate_basic_4holes/**
→ SolidWorksReleasePackageBuilder
→ artifacts/plate_basic_4holes.SLDPRT
→ artifacts/plate_basic_4holes.STEP
→ artifacts/plate_basic_4holes.SLDDRW
→ artifacts/plate_basic_4holes.pdf
→ reports/*.json
→ release_manifest.json
→ package_quality_report.json
→ release_summary.md
```

发布包输出目录固定为 `output/solidworks/release/plate_basic_4holes/<timestamp>/`。质量检查只验证文件存在、大小大于 0、包内路径正确、PDF 存在，以及报告中的 `final_status` 和失败报告的 `failure_stage`。若源工程图、PDF 或报告缺失，仍必须生成 `release_manifest.json`、`package_quality_report.json` 和 `release_summary.md`，并把失败阶段记录为 `source_artifacts_missing` 或 `source_report_missing`。

V1.4 默认 self-check 可以执行发布包收集，因为它只读写文件系统，不调用 COM、不连接 SolidWorks、不触发任何真实 CAD smoke test。V1.4 不做 BOM、装配图、批量出图、国标模板美化、复杂图纸审查、几何 OCR、PDF 视觉识别或 V1.5 内容。

## V1.5 真实 CAD 主工作流集成

V1.5 不新增 SolidWorks 子功能，只把已有 `plate_basic_4holes` 真实 CAD 能力接入平台主流程。Worker 仍不暴露给 Agent、Gateway 或 LLM；`SolidWorksWorkflowRouter` 负责把显式 flag、结构化上下文或具体 `plate_basic_4holes` 请求转换为主流程请求，`SolidWorksMainWorkflowRunner` 负责把 Skill、Validator、Worker、Reviewer 和 QualityGate 串成一个 `SequentialWorkflowEngine` 工作流。泛化提到 `SolidWorks` 不应单独触发 CAD 主流程。

```text
ChiefEngineerOrchestrator
→ SolidWorksWorkflowRouter
→ SolidWorksMainWorkflowRunner
→ SolidWorksBuildPlanSkill
→ SolidWorksBuildPlanValidator
→ FakeSolidWorksWorker 或 RealSolidWorksWorker
→ SolidWorksArtifactValidator
→ SolidWorksBuildPlanReviewer
→ QualityGate
```

本地交互式默认路径使用 `RealSolidWorksWorker`。以下任一条件成立时改用 `FakeSolidWorksWorker` 或在连接前拒绝：

- 请求上下文包含 `dry_run=true`。
- 环境变量包含 `SW_DISABLE_REAL_EXECUTION=true`。
- 当前为 CI 或单元测试环境。
- 环境变量包含 `SW_FORCE_FAKE_WORKER=true`。

V2.0 不再要求旧请求级启用确认或 `SW_ENABLE_REAL_EXECUTION`。本地交互式且 `dry_run=false` 时默认选择真实路径；只有 dry-run、显式禁用、CI、单元测试或强制 Fake Worker 时保持 fake 路径，并在结果中记录 `real_cad_executed=false`。主流程返回的 artifact 元数据必须包含 `real_cad_executed`、`quality_gate_passed`、`execution_mode` 和输出目录。

## V1.5 发布包语义修正

`release_manifest.json` 和 `package_quality_report.json` 必须区分以下字段：

- `package_build_status`：打包流程是否成功，只代表文件复制、manifest、summary 和 quality report 是否生成。
- `source_reports_checked`：是否已读取所有源报告的 `final_status`。
- `all_source_reports_passed`：所有源报告是否均为 `Passed`。
- `source_report_failures`：`final_status=Failed` 的源报告列表。
- `source_report_warnings`：非 `Passed` 且非 `Failed` 的源报告列表。
- `deliverable_status`：最终是否可交付。

如果任一源报告 `final_status=Failed`，则 `package_build_status` 仍可以是 `Passed`，但 `all_source_reports_passed=false`、`deliverable_status=NotDeliverable`、`final_status=Failed`，并且 `source_report_failures` 必须列出失败报告。不得再把发布包打包成功误解释为工程交付通过。

## 禁止事项

- V2.0 本地交互式且 `dry_run=false` 时默认启动或连接 SolidWorks；self-check、CI、单元测试、dry-run 和显式禁用不得启动。
- 不让 Agent、Gateway、LLM 直接调用 Worker。
- 不复制第三方 Python COM 脚本。
- 不把 COM 类型泄漏到平台 Contracts。
- 不在诊断 Runner 未验证时回填主 Worker。

## V1.7 真实主工作流端到端验收

V1.7 使用 `SolidWorksMainWorkflowRunner` 的受控完整 operation 串联既有 Build、Drawing、Dimension、TitleBlock 与 ReleasePackage；不新增 SolidWorks API，也不直接调用 Builder。入口为：

```powershell
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_plate_request.json
```

CLI 只经 Gateway 调用公开的 `chief-engineer`。`SolidWorksWorkflowRouter` 识别结构化 operation 和 part_type，并把请求、交付 flag 和输出路径传递给主工作流。V2.0 不再要求请求或环境启用确认；执行被 dry-run、显式禁用、CI、单元测试或强制 Fake Worker 关闭时，报告使用明确禁用原因，不得把 Fake 结果作为真实验收通过。

四阶段真实源输出继续写入 `output/solidworks/real/`。

- 每一步仍经过 `SolidWorksArtifactValidator`、Reviewer 和 QualityGate。
- ReleasePackage 写入 `output/solidworks/e2e/plate_basic_4holes/<timestamp>/`。
- 发布包只复制当前 request 的显式源集合：四份阶段报告与最终 SLDPRT、STEP、SLDDRW、PDF。
- 发布包不扫描历史最新目录，也不要求或引用 `SmokeRunner` 的 `diagnostic_report.json` 作为最终成功依据。

最终包使用 `SolidWorksE2EReleasePackageBuilder` 写入 `artifacts/`、`reports/`、`release_manifest.json`、`package_quality_report.json`、`release_summary.md` 与 `latest_real_outputs.md`。只有四阶段报告均 Passed、四项真实执行证据均匹配期望 mode、全部产物非空且总体 QualityGate Passed 时，`all_source_reports_passed=true` 且 `deliverable_status=Deliverable`。文件创建或包复制成功本身不构成通过。

## V1.7-REAL-AUTH 本地授权执行

项目根目录下未提交的 `config/solidworks.local.json` 只提供可选模板、可见性和超时，不再承担授权。CLI 默认 `dry_run=false` 并显示 SolidWorks，仍只经 Gateway、`chief-engineer`、WorkflowEngine 和 Router 进入 Worker，不能直接调用 Worker、Builder 或 SmokeRunner。

Worker 仍按既有预检、COM 连接、产物校验、Reviewer 与 QualityGate 执行。报告中的 `solidworks_launch_attempted` 仅在 Worker 已开始连接时为真；`real_worker_invoked` 仅在真实 Worker 已收到请求时为真，二者都不能由文件存在替代。

## V1.8 零件族 Worker 执行

### 目标与适用范围

V1.8 将 Worker 从单一四孔板特判升级为可注册的零件族执行边界。`plate_basic_4holes` 继续使用已验证的真实 Builder；`flange_basic` 和 `shaft_basic` 本轮必须能生成专用 BuildPlan 并通过 dry-run，但不得仅凭候选 API 进入真实主流程。

### 输入与输出

输入是已通过通用和零件族 Validator 的 `CADModelSpec` 与 BuildPlan。输出是该族的 Worker result、SLDPRT / STEP 或 dry-run 占位产物、`build_report.json`、可行动 `failure_stage` 和 ArtifactValidator 结果。

### 组件与参数

- `PartTypeRegistry` 保存 `IPartFamilyDefinition` 映射，`PartFamilyBuilderRegistry` 保存 `IPartFamilyBuilder` 映射；重复键和缺失 Builder 必须在启动或计划阶段失败。
- `PlateBasic4HolesDefinition` 处理 `length_mm`、`width_mm`、`thickness_mm`、`hole_count`、`hole_diameter_mm` 及孔位特征；`PlateBasic4HolesPartFamilyBuilder` 负责通用 dry-run，真实路径继续由 `RealSolidWorksWorker` 委托已验证的 plate Builder。
- `FlangeBasicDefinition` 处理 `outer_diameter_mm`、`inner_diameter_mm`、`thickness_mm`、`bolt_hole_count`、`bolt_hole_diameter_mm`、`bolt_circle_diameter_mm`，对应 `FlangeFeatureBuilder` 的 dry-run 计划。
- `ShaftBasicDefinition` 处理 `diameter_mm`、`length_mm`、`optional_step_diameters`、`optional_step_lengths`，对应 `ShaftFeatureBuilder` 的 dry-run 计划。

每个 Definition 必须自带 Schema、Validator、BuildPlan 生成逻辑和失败语义；共享 Worker 只做调度和安全边界，不应识别每个零件族的几何细节。

### 执行步骤

```text
结构化输入
→ CADModelSpec
→ PartTypeRegistry
→ 零件族 Validator
→ BuildPlan
→ IPartFamilyBuilder
→ FakeSolidWorksWorker 或 RealSolidWorksWorker
→ SolidWorksArtifactValidator
→ Drawing
→ Reviewer
→ QualityGate
→ ReleasePackage
```

不受支持的类型以 `unsupported_part_type` 返回；缺少或非法参数以 `missing_required_parameter` 或 `invalid_parameter_value` 返回。这三类结果都不得创建 Worker request、调用 `ConnectAsync` 或启动 SolidWorks。

真实执行仍只能由 `ChiefEngineerOrchestrator` → `WorkflowEngine` → Router → Worker → Validator → Reviewer → `QualityGate` 路径进入。工程图和发布包必须使用当次零件族产物的显式路径，不得扫描历史 latest。

### 验证标准与常见失败

- `plate_basic_4holes` 必须通过既有真实能力回归和默认 dry-run。
- `flange_basic` 和 `shaft_basic` 必须分别通过 dry-run，并且产物名、BuildPlan operation 和报告不得被写死为 `plate_basic_4holes`。
- 定义、计划和 Builder 缺失分别使用 `part_family_definition_missing`、`build_plan_generation_failed`、`part_family_builder_missing`。
- 法兰和轴的 Builder 失败分别使用 `flange_build_failed` 和 `shaft_build_failed`；产物校验失败使用 `artifact_validation_failed`。
- 默认 self-check 不连接 COM，`real_cad_part_family_default_disabled` 必须为 `true`。

### 禁止事项

- 不在 Worker 中增加大型 `switch(part_type)`，不使用分散字符串特判执行零件族几何。
- 不把 `flange_basic` 或 `shaft_basic` dry-run 写成真实 SolidWorks 成功或可交付。
- 不实现装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9。

## V1.9 Phase 1 真实 build-only Worker 执行

### 目标与适用范围

`RealSolidWorksWorker` 对 `flange_basic` 和 `shaft_basic` 执行 build-only，使用 `PartFamilyBuilderRegistry` 解析真实 Builder，并统一管理会话、保存、STEP 导出和报告。两族不进入 Drawing；plate 完整包回归保持不变。

### 输入与输出

输入为已校验 BuildPlan、`PartFamilyBuildContext`、统一运行策略和对应 API evidence。输出 `PartFamilyBuildResult`、真实 SLDPRT / STEP、`build_report.json`、端到端报告、Validator / Reviewer / QualityGate 结果和 build-only 发布包。

### 执行链和安全边界

```text
结构化输入
→ Gateway / chief-engineer
→ ChiefEngineerOrchestrator
→ WorkflowEngine
→ SolidWorksWorkflowRouter
→ PartTypeRegistry
→ PartFamilyBuilderRegistry
→ RealSolidWorksWorker
→ ArtifactValidator
→ Reviewer
→ QualityGate
→ build-only ReleasePackage
```

V2.0 统一策略为本地交互式且 `dry_run=false` 时默认真实执行；dry-run、显式禁用、CI、单元测试或强制 Fake Worker 时不连接 COM。self-check 不连接 COM。

真实 SolidWorks 操作全局串行，首次验收顺序为 flange → shaft。前一任务释放会话并写完报告后，才允许后一任务连接。

### 构建策略

- flange：外圆 `CreateCircle` 加 `FeatureExtrusion2`；中心孔独立活动草图 `FeatureCut4`；全部螺栓孔单草图 `FeatureCut4`。不用 `HoleWizard` 或圆周阵列。
- shaft：`CreateLine` 闭合轮廓，`CreateCenterLine` 中心线，selection mark `16`，`FeatureRevolve2` 360°。偏移多段拉伸仅作 backlog，本轮不混用。

### 产物与报告

最终目录为 `output/solidworks/e2e/<part_type>/<timestamp>/`，必需内容为：

```text
artifacts/<part_type>.SLDPRT
artifacts/<part_type>.STEP
reports/build_report.json
reports/e2e_execution_report.json
release_manifest.json
```

`build_report.json` 要记录零件族、真实模式、执行模式、授权请求、连接、保存、导出、API evidence、`failure_stage` 和最终状态。执行模式应分别为 `RealBuildFlangeBasic` 和 `RealBuildShaftBasic`。

### 验证和禁止事项

Phase 1 入场时 API 证据只足以进入独立 diagnostic，实际 smoke 结果和路径尚待回填；该条件现已由下述 Phase 2 运行记录关闭。真实验收必须使用主工作流程，不允许直接 Builder / SmokeRunner。禁止 flange / shaft 自动工程图，禁止并发 COM，禁止缺失 Validator / Reviewer / QualityGate 仍生成可交付包，禁止进入 V2.0。

## V1.9 Phase 2 Worker 运行记录

Phase 1 的待运行状态已经关闭。flange 与 shaft 专用 diagnostic 都为 `CandidatePassed`，并分别通过 100 分规则审查、特征树检查、四视图检查和非空 SLDPRT / STEP 校验。最终 CLI 主流程目录为：

```text
output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/
output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/
```

两次 `e2e_execution_report.json` 均证明 `Gateway`、`ChiefEngineerOrchestrator`、`WorkflowEngine`、`Router`、`Registry`、`RealSolidWorksWorker`、`ArtifactValidator`、`Reviewer`、`QualityGate` 与发布包通过，最终状态为 `Passed`、`Deliverable`。最终 `build_report.json` 还记录了对应的 V1.9 专用诊断、视觉审查和主流程通过元数据。

plate 完整包在 `output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260720_082027_397-bd86bc56b48349c69db5f8173c1b3d85/` 回归为 `Passed`、`Deliverable`、QualityGate `Passed`，其工程图能力没有退化。

保存修复沿用 plate 已验证的 `SaveAs3` / `SaveAs` 策略。源文件瞬时读锁只在复制和目标校验已经成功时降级为 warning；其他 `artifact_copy_failed` 必须继续阻断。body count 与 theoretical volume 自动核验留作 Improvement，本阶段不生成 flange / shaft 工程图，也不进入 V2.0。

## V2.0-A Worker 边界

V2.0-A 的 canonical `CADModelSpec`、`SketchDefinition`、`FeatureDefinition`、`FeatureGraph` 和 `BuildPlanCompiler` 只负责生成并验证描述性 `SolidWorksBuildPlan`。这条通用链默认走 dry-run，不连接 COM。

`FeatureGraph` 是 Schema 到 BuildPlan 的唯一特征来源。缺失依赖、依赖环、非法顺序、未知草图、实体、约束或特征类型必须在 Worker 调度前失败。

V1.9 的 `plate_basic_4holes`、`flange_basic`、`shaft_basic` 专用真实 Builder 继续由 `PartFamilyBuilderRegistry` 解析，真实能力和既有验收不回退。但它们只证明固定零件族语义，不能证明任意 FeatureGraph 或十类通用 Feature Handler 已经真实执行。

通用 `extrude_boss`、`extrude_cut`、`revolve_boss`、`revolve_cut`、`fillet`、`chamfer`、`hole`、`linear_pattern`、`circular_pattern`、`mirror` 到真实 SolidWorks COM 的 Handler 延期到 V2.0-B。本轮不得增加通用执行分支、装配体或队列。

V2.0 的默认真实策略仍适用于已受控的本地交互主流程；它不允许 V2.0-A 的通用图绕过 dry-run 边界。self-check、单元测试和 dry-run 始终不启动 SolidWorks。
