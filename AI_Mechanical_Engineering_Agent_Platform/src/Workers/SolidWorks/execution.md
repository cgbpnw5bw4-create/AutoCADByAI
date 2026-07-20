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

默认走 `FakeSolidWorksWorker`。真实执行必须先通过 `SolidWorksEnvironmentValidator`，再由 `SolidWorksSessionManager` 连接，最后由 `SolidWorksPlateFeatureBuilder` 执行受控建模步骤。

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

真实工程图默认关闭。只有 `SW_ENABLE_REAL_EXECUTION=true` 且 `SW_REAL_DRAWING_SMOKE_TEST=true` 时，self-check 才允许进入工程图 smoke test。严格模式需要额外设置 `SW_STRICT_REAL_DRAWING_TEST=true`。

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

真实尺寸标注默认关闭。只有同时设置 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_DIMENSION_SMOKE_TEST=true` 时，self-check 才允许进入真实尺寸 smoke test。严格模式需要额外设置 `SW_STRICT_REAL_DRAWING_DIMENSION_TEST=true`。

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

真实标题栏测试默认关闭。只有同时设置 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_DRAWING_TITLE_BLOCK_SMOKE_TEST=true` 时，self-check 才允许进入真实标题栏 smoke test。严格模式需要额外设置 `SW_STRICT_REAL_DRAWING_TITLE_BLOCK_TEST=true`。

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

默认路径仍使用 `FakeSolidWorksWorker`，不会启动 SolidWorks。真实路径必须同时满足：

- 请求上下文包含 `allow_real_cad_execution=true`。
- 请求上下文包含 `dry_run=false`。
- 环境变量包含 `SW_ENABLE_REAL_EXECUTION=true`。

若请求级开关或环境级开关任一缺失，主流程必须保持 fake / dry-run 路径，并在结果中记录 `real_cad_executed=false`。主流程返回的 artifact 元数据必须包含 `real_cad_executed`、`quality_gate_passed`、`execution_mode` 和输出目录。

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

- 不默认启动 SolidWorks。
- 不让 Agent、Gateway、LLM 直接调用 Worker。
- 不复制第三方 Python COM 脚本。
- 不把 COM 类型泄漏到平台 Contracts。
- 不在诊断 Runner 未验证时回填主 Worker。

## V1.7 真实主工作流端到端验收

V1.7 使用 `SolidWorksMainWorkflowRunner` 的受控完整 operation 串联既有 Build、Drawing、Dimension、TitleBlock 与 ReleasePackage；不新增 SolidWorks API，也不直接调用 Builder。入口为：

```powershell
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_plate_request.json
```

CLI 只经 Gateway 调用公开的 `chief-engineer`。`SolidWorksWorkflowRouter` 必须识别 `operation=build_complete_drawing_package` 和 `part_type=plate_basic_4holes`，并把请求、四个 generate flag 和输出路径传递给主工作流。真实路径必须同时满足请求 `allow_real_cad_execution=true`、`dry_run=false`，环境 `SW_ENABLE_REAL_EXECUTION=true`、`SW_REAL_MAIN_WORKFLOW_TEST=true`。缺少任何一项都必须写出 `e2e_execution_report.json` 并以 `real_execution_confirmation_missing` 失败；不得降级 Fake Worker 后作为验收通过。

四阶段真实源输出继续写入 `output/solidworks/real/`。

- 每一步仍经过 `SolidWorksArtifactValidator`、Reviewer 和 QualityGate。
- ReleasePackage 写入 `output/solidworks/e2e/plate_basic_4holes/<timestamp>/`。
- 发布包只复制当前 request 的显式源集合：四份阶段报告与最终 SLDPRT、STEP、SLDDRW、PDF。
- 发布包不扫描历史最新目录，也不要求或引用 `SmokeRunner` 的 `diagnostic_report.json` 作为最终成功依据。

最终包使用 `SolidWorksE2EReleasePackageBuilder` 写入 `artifacts/`、`reports/`、`release_manifest.json`、`package_quality_report.json`、`release_summary.md` 与 `latest_real_outputs.md`。只有四阶段报告均 Passed、四项真实执行证据均匹配期望 mode、全部产物非空且总体 QualityGate Passed 时，`all_source_reports_passed=true` 且 `deliverable_status=Deliverable`。文件创建或包复制成功本身不构成通过。

## V1.7-REAL-AUTH 本地授权执行

真实主流程的唯一授权文件为项目根目录下未提交的 `config/solidworks.local.json`。只有 `real_execution_authorized=true` 且来源为 `LocalDevelopmentProfile` 时，CLI 才自动设置真实执行、关闭 dry-run、应用零件/工程图模板并默认显示 SolidWorks。CLI 仍只经 Gateway、`chief-engineer`、WorkflowEngine 和 Router 进入 Worker，不能直接调用 Worker、Builder 或 SmokeRunner。

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
