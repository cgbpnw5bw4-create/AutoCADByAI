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

每次受控回调结束都会只关闭本次工作流创建或使用的文档，再在有上限的超时内断开 COM 会话。不会调用 `ExitApp`：该应用可能是用户已打开的可见 SolidWorks 会话，Worker 不得终止用户拥有的进程。关闭失败必须记录 `solidworks_document_close_warning`，不能静默吞掉；发布包只按精确产物文件名收集，不纳入 `~$` 锁文件。

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

## V2.0-C Feature Adapter 真实执行

### 目标与分层

V2.0-C 将通用 Feature Handler 保持为纯逻辑，把所有 SolidWorks COM 调用放入 `RealSolidWorksFeatureAdapter`。`ISolidWorksFeatureAdapter` 是可注入边界，`RealSolidWorksWorker` 负责会话、安全检查、依赖顺序、Adapter 生命周期、结果聚合、保存和 STEP 导出。

```text
FeatureGraph / SolidWorksBuildPlan
→ FeatureHandlerRegistry
→ 纯逻辑 Handler 校验与命令
→ ISolidWorksFeatureAdapter
→ RealSolidWorksFeatureAdapter
→ RealSolidWorksWorker 聚合同次结果
→ SolidWorksArtifactValidator
→ Reviewer
→ QualityGate
```

Handler 工程不得引用 SolidWorks Interop，不得持有 COM 对象、连接应用程序、选择文档或保存文件。测试通过 fake Adapter 验证失败和防假成功行为，真实 Worker 只在安全条件与 evidence 门禁均通过时使用真实 Adapter。

### 最小执行候选

| Handler | V2.0-C 候选 | 明确不做 |
|---|---|---|
| Sketch | line、rectangle、circle，受控标准基准 | 任意面、arc、slot、通用约束/尺寸执行 |
| Extrude | 正深度 blind extrude | `mid_plane`、thin、draft、复杂 scope |
| Cut | 正深度 blind `FeatureCut4` | `through_all` 猜测、normal cut、thin、多实体 scope |
| Hole | 独立圆草图加正深度 blind `FeatureCut4` | `SimpleHole2`、Hole Wizard、未验证标准/类型枚举 |

专用 diagnostic 未运行和审查前，状态只能是 `diagnostic_candidate` / `unverified`。生产真实计划遇到任一未验证能力时返回 `feature_api_unverified`，不得连接或继续执行。

### 结果校验与防假成功

每个 Adapter 调用必须返回结构化结果。Worker 不得只按“未抛异常”判定成功，还必须检查 Feature/草图实体对象、依赖顺序、重建状态和操作标识。任一返回无效使用 `feature_result_invalid`；不允许跳过失败节点或继续保存部分模型。

保存和 STEP 导出后必须确认当次绝对路径、文件存在且非空。SLDPRT、STEP 或报告任一缺失使用 `feature_artifact_missing`。历史文件、陈旧 timestamp 和其他运行产物不能补齐本次结果。

### 专用 diagnostic 输出

```text
output/solidworks/features/<timestamp>/
├── model.SLDPRT
├── model.STEP
└── feature_execution_report.json
```

报告记录运行和源码修订、SolidWorks/Adapter/Handler 版本、证据状态、特征顺序、参数、API 返回、重建、失败阶段、产物路径和大小。diagnostic 成功只允许进入 evidence 审查，不生成最终可交付结论。

### 最终验收与 self-check

最终验收只从命令行主入口 `run-cad-workflow` 进入，并依次经过总工程师编排、工作流引擎、路由、执行器、校验器、复核器、质量门和发布包。直接调用适配器、特征处理器或专用诊断均不能替代这条受控主流程。

```text
feature_adapter_layer_exists
feature_handler_no_direct_com_access
solidworks_feature_adapter_exists
sketch_real_execution_supported
extrude_real_execution_supported
cut_real_execution_supported
hole_real_execution_supported
feature_pipeline_end_to_end_supported
feature_result_validation_supported
feature_fake_success_guard_supported
v2_0_c_documented
markdown_chinese_check_passed
```

self-check、CI、单元测试和 dry-run 不启动 SolidWorks。禁止 Handler COM、未验证生产执行、`SimpleHole2` / Hole Wizard、空结果/空产物成功、diagnostic 冒充主流程或进入 V2.0-D。

### 2026-07-30 diagnostic 回填

旧 run `output/solidworks/features/20260730_073759_9143941/` 的 Cut/Hole 虽返回非空 Feature 且重建通过，但人工几何复核没有孔，因此不得保留为 verified。该反例要求 Worker 的防假成功不能只依赖对象非空、重建布尔或文件存在。

权威 run 为 `output/solidworks/features/20260730_085830_6592380/`：

- SolidWorks `33.5.0`；Handler/Adapter `2.0-c.2`；复合源码修订 `feature-execution-source-sha256:71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918`。
- `model.SLDPRT` 73416 bytes，SHA256 `E411188A101E49EFB1BD835E9BEF3A16EA0EE3BB124A873FFB96EDC5E72012E3`。
- `model.STEP` 26403 bytes，SHA256 `EF532158373D512CF31A76FE930CD90FBF21A913E52608CF9A04805F0590CE06`。
- Boss 体积 `0→5.9999999999999995E-05` m³。
- Cut 体积 `5.9999999999999995E-05→5.9214601836602546E-05` m³。
- Hole 体积 `5.9214601836602546E-05→5.84292036732051E-05` m³。
- 7 个特征报告均完成结果、重建和几何变化校验；最终审查的 100/`pass`、一个 `Extrusion`、两个 `ICE` 与人工双孔确认以同次 E2E 报告为准。

只把 Sketch/Boss/Cut/Hole 的同版本、同 Adapter、同精确 profile 提升为 `verified`。`through_all`、`mid_plane`、原生 `SimpleHole2` / Hole Wizard、任意面仍为 `unverified`。该 diagnostic 自身保持 `CandidatePassed` / `NotDeliverable`，只提供 evidence，不承担最终交付判定。

### 2026-07-30 最终主流程验收

生产 preflight 在 `ConnectAsync` 前校验未知参数、精确 profile、复合修订、diagnostic 绑定和 Hole 的“直接依赖单圆且直径匹配”图关系；未知参数返回 `invalid_feature_parameter`，其余不匹配返回 `feature_api_unverified`。连接后再以实际 SolidWorks `33.5.0` 逐 Handler 复核运行时证据版本，版本不匹配时不调用建模 API。

最终 E2E 目录为 `output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260730_090151_162-1b16c3982731423a8ae9a93f1db2dbbe/`。同次 `e2e_execution_report.json` 和 `package_quality_report.json` 记录 `final_status=Passed`、QualityGate `Passed`、`all_source_reports_passed=true`、`deliverable_status=Deliverable`。

`feature_execution_report.json` 的预期/执行特征为 7/7，`all_features_executed`、`all_result_objects_validated`、`all_rebuilds_passed`、`all_geometry_changes_validated`、`artifacts_validated` 均为 `true`，Boss/Cut/Hole 三段体积与权威 diagnostic 相同。最终 SLDPRT 为 73830 bytes，SHA256 `045A5CF2C445F66B1CE2502065F84805A40CF217600DAD213C68D9EEFBA78612`；STEP 为 26399 bytes，SHA256 `DCAB84553885D804AB961E4BBABB691A7A47C6962D030781211B7B777E1EF7BB`。

`review_active_source/feature_pipeline_plate_review_report.json` 为 100 分、`pass`，特征树含一个 `Extrusion` 和两个 `ICE`，人工确认两个孔。因此四个精确 profile 已经经过完整主流程并得到 `Passed` / `Deliverable` 结论；这不扩大 evidence 边界，也不进入 V2.0-D。

## V2.0-D Worker 参数重建与几何读取

RealSolidWorksWorker 的 V2.0-D 受控顺序为：

~~~text
CADModelSpec
  -> ModelUpdateService
  -> BuildPlanCompiler
  -> FeatureExecutionPipeline
  -> RealSolidWorksWorker / SolidWorks Rebuild
  -> RealSolidWorksGeometryReader
  -> GeometryValidator
  -> Artifact Validator / Reviewer / QualityGate
~~~

Worker 只执行编译后的既有 `FeatureGraph`，并通过 `FeatureHandlerRegistry` 和已注册 `FeatureHandler` 到特征适配器；不得直接调用构建器、另建 CAD Feature、另建零件族或把 `COM` 业务逻辑移入 Handler。参数更新必须保留并回传 `old_parameters`、`new_parameters`、`changed_features`、`rebuild_result`，随后在当前受控 `SolidWorks` 会话中完成一次真实重建。

RealSolidWorksGeometryReader 是读取当前模型真实几何的唯一 COM 边界。它在 Rebuild 后读取 BoundingBox、Body 数量、Volume、可用 MassProperty、特征树和精确尺寸，输出无 COM 引用的 DTO；GeometryValidator 再独立检查长度、孔径/轴径、Sketch、Extrude、Cut、Hole 与预期是否一致。只获得 COM 成功、非空 Feature、SLDPRT/STEP 存在均不得报告成功。

每次执行必须写出同次 `geometry_validation_report.json` 与 `rebuild_report.json`，并把它们交给 `Artifact Validator`、`Reviewer`、`QualityGate` 和发布包。失败必须停止交付并写入 `rebuild_failed`、`geometry_read_failed`、`bounding_box_invalid`、`volume_validation_failed`、`parameter_geometry_mismatch`、`feature_missing_after_rebuild` 或 `geometry_report_failed`；不得回退到历史产物。

真实验收仅允许：

~~~powershell
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/parameter_update_plate.json
~~~

该输入需先建模 160×80×12，再更新为 200×100×15，并以真实几何验证变化。plate_basic_4holes 必须复用现有经证 Feature 类型且逐个证明四孔；模型名或 hole_count 不可代替几何读取。V2.0-E 只能复用该受控链，最终 QualityGate 未通过前不得标记为可交付。
## V2.0-D 三圆切除执行门禁

参数重建请求进入 `RealSolidWorksWorker` 时，执行顺序固定为：既有 `FeatureHandlerRegistry` 完整预检 → `V20DThreeCircleCutEvidencePolicy` 精确 profile / SHA / 候选诊断预检 → SolidWorks 连接 → `FeatureExecutionPipeline` → rebuild → GeometryReader → GeometryValidator。三圆证据失败时不会建立 COM 连接，也不会改走零件族 Builder、Fake Worker 或旧产物。

此门禁只授权 `examples/parameter_update_plate.json` 的 160×80×12 到 200×100×15 参数更新链；它保留既有 sketch、extrude_boss、extrude_cut、hole Handler，且要求 `cut_profile` 三圆和 `hole_profile` 单圆均为直径 10 mm、20 mm 边距。最终验收仍只能运行 `run-cad-workflow`。

## V2.1-A 夹套真实执行

`JacketFeatureBuilder` 仅接受 `RealBuildJacketBasic` 模式。它在 TopPlane 创建外圆并用 `FeatureExtrusion2` 形成指定长度的圆柱体，再在 TopPlane 创建同轴内圆并用 `FeatureCut4` 盲切两倍轴向长度；所有长度在进入 COM 前由毫米转换为米。Builder 随后严格重建并用真实 GeometryReader 校验单实体、外包络、内外径和体积；STEP 落盘后还必须通过 ISO 10303-21 内容检查。任一步失败都会写失败构建报告，不会生成成功 Artifact。

当前结构化运行时证据未激活，真实 Worker 会在连接 COM 前以 `part_family_api_evidence_insufficient` 拒绝；dry-run 不受影响。重新采集受控 diagnostic 并恢复授权后，真实命令为 `dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_jacket_request.json`。不得以独立 diagnostic、文件存在或 COM 非空返回替代该主流程。
