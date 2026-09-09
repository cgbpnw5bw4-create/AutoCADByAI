# CADModeling API 证据规则

## 基本原则

- 不得臆造 API。
- 优先查官方 `SolidWorks API Help`。
- 其次查本地 SDK、宏录制和已验证诊断报告。
- 再参考 `references/external/solidworks-automation-skill-analysis.md`。
- 如果用户手动放入 `references/external/solidworks-automation-skill`，只能只读分析。
- 不能复制 `scripts` 源码。
- 不能把第三方 Python 脚本作为生产路径。
- 需要生成 `ApiEvidenceReport`。

## 证据字段

`ApiEvidenceReport` 必须记录：

- `official_api_sources`
- `local_reference_sources`
- `third_party_reference_sources`
- `extracted_api_candidates`
- `selected_api_strategy`
- `rejected_strategies`
- `final_recommendation`

## 当前重点 API

- `SelectByID2`
- `InsertSketch`
- `CreateCornerRectangle` 或等价矩形草图 API
- `CreateCircleByRadius`
- `FeatureExtrusion`、`FeatureExtrusion2` 或等价基体拉伸 API
- `FeatureCut3`、`FeatureCut4` 或等价切除 API
- `SaveAs`、`SaveAs2`、`SaveAs3`
- `ActivateDoc`、`ActiveDoc`
- `ClearSelection2`
- `NewDocument`
- `OpenDoc6`
- `CreateDrawViewFromModelView3`
- `CreateLinearDim4`
- `ICreateDiamDim4`
- `AddDimension2`
- `CustomPropertyManager`
- `Add3`
- `Set2`
- `Get6`
- `GetCurrentSheet`
- `GetProperties2`
- `IModelDocExtension.SaveAs`
- `GetExportFileData`
- `IExportPdfData.SetSheets`

## `cut_holes_failed` 当前证据结论

切孔失败优先检查活动孔草图状态和 `FeatureCut4` 参数，而不是把 `FeatureExtrusion2` 当切孔候选。只有活动孔草图 `FeatureCut4` 路径失败后，孔草图退出并按“草图 `Feature` 对象、草图对象、轮廓、区域、线段、`FeatureByName`、`SelectByID2("SKETCH")`”的顺序重建选择集，并且每个 fallback 切孔候选前都要重新选择。

若 `FeatureCut4` 活动草图路径与 fallback 候选都失败，不能继续盲改 API，应输出 `api_evidence_insufficient`，并要求用户提供最小宏录制结果作为下一轮证据。

## V1.0-B-REPAIR 最新切孔证据

用户第二份宏已经修正上一轮判断：`FeatureExtrusion2` 负责板件基体拉伸，孔由后续孔草图上的 `FeatureCut4` 切除。CADModeling 模块在生成、审查或修复 `plate_basic_4holes` 时，必须把该顺序作为当前主证据：

1. `SolidWorksBuildPlanSkill` 仍只生成结构化计划，不直接调用 Worker。
2. `RealSolidWorksWorker` 通过 `SolidWorksPlateFeatureBuilder.CreateBasePlate` 创建基体。
3. `RealSolidWorksWorker` 通过 `SolidWorksPlateFeatureBuilder.CreateThroughHoles` 执行活动孔草图 `FeatureCut4`。
4. `FeatureExtrusion2` 不得再被当成切孔候选。
5. 若 `FeatureCut4` 仍失败，必须补充完整文本宏或官方 API 参数证据后再改。

## 使用边界

证据可以指导封装重写，但不能绕过 `Worker`、`Validator`、`Reviewer` 和 `QualityGate`。未验证 API 不能直接进入主 Worker。

## V1.1 工程图 API 证据

V1.1 工程图只允许基础视图能力。当前候选顺序是：

1. `OpenDoc6` 打开 `plate_basic_4holes.SLDPRT`。
2. `ActivateDoc3` 激活源零件。
3. `NewDocument` 使用 `.drwdot` 创建 Drawing。
4. `CreateDrawViewFromModelView3` 创建 `*Front`、`*Top`、`*Right`、`*Isometric`。
5. `IModelDocExtension.SaveAs` 保存 `SLDDRW`。
6. 激活 Drawing 后通过 `IModelDocExtension.SaveAs` 导出 PDF，必要时使用 `GetExportFileData` 和 `IExportPdfData.SetSheets`。

官方证据已覆盖 `OpenDoc6`、`ActivateDoc3`、`NewDocument`、`CreateDrawViewFromModelView3`、`IModelDocExtension.SaveAs`、`GetExportFileData` 和 `IExportPdfData.SetSheets`。详细 URL 记录在 `src/Workers/SolidWorks/api_evidence.md`。

任何视图创建、保存或 PDF 导出失败，都必须先生成 `drawing_report.json`，再依据官方 API 或宏录制证据修复。不能把工程图宏直接作为生产路径。

## V1.2 工程图尺寸 API 证据

V1.2 工程图尺寸只允许最小基础尺寸标注。当前候选顺序是：

1. `OpenDoc6` 打开 V1.1 的 `plate_basic_4holes.SLDDRW`。
2. 读取并确认 Front、Top、Right、Isometric 四个视图。
3. `CreateLinearDim4` 创建 160 mm 长度、80 mm 宽度、12 mm 厚度、120 mm 与 40 mm 孔中心距。
4. `ICreateDiamDim4` 创建 Φ10 孔径尺寸。
5. `IModelDocExtension.SaveAs` 保存带尺寸 SLDDRW，并导出 PDF。

官方证据已覆盖 `CreateLinearDim4`、`ICreateDiamDim4`、`AddDimension2`、`InsertModelDimensions` 和 `IView.GetVisibleEntities2`。详细 URL 记录在 `src/Workers/SolidWorks/api_evidence.md`。

当前主路径拒绝 `InsertModelDimensions`，因为它会接近自动导入模型尺寸，超出 V1.2 范围。`AddDimension2` 和 `IView.GetVisibleEntities2` 暂作为后续关联尺寸候选，不作为本轮生产路径。任何尺寸创建、保存或 PDF 导出失败，都必须先生成 `dimension_report.json`，再依据官方 API 或宏录制证据修复。

## V1.3 工程图标题栏 API 证据

V1.3 工程图标题栏只允许最小标题栏/图纸属性信息。当前候选顺序是：

1. `OpenDoc6` 打开 V1.2 的 `plate_basic_4holes_dimensioned.SLDDRW`。
2. `GetCurrentSheet` 取得当前 Sheet。
3. `GetProperties2` 读取图纸比例；不可读时记录为 `auto` 或明确失败。
4. `CustomPropertyManager` 获取文档级自定义属性管理器。
5. `Add3`、`Set2`、`Get6` 写入并验证 `PartName`、`DrawingNumber`、`Material`、`Scale`、`DrawingDate`、`Revision`。
6. `ForceRebuild3` 或 `EditRebuild3` 刷新标题栏字段引用。
7. `IModelDocExtension.SaveAs` 保存带标题栏信息 SLDDRW，并导出 PDF。

官方证据已覆盖 `CustomPropertyManager`、`Add3`、`Set2`、`Get6`、`GetCurrentSheet`、`GetProperties2`、`EditTemplate` 和标题栏数据输入说明。详细 URL 记录在 `src/Workers/SolidWorks/api_evidence.md`。

当前主路径拒绝复杂国标模板几何编辑、BOM、明细栏、公差系统和宏生产路径。任何标题栏属性写入、图纸属性读取、刷新、保存或 PDF 导出失败，都必须先生成 `title_block_report.json`，再依据官方 API 或宏录制证据修复。

## V1.4 工程发布包证据边界

V1.4 只整理已经存在的 SLDPRT、STEP、SLDDRW、PDF 和 JSON 报告，并生成 `release_manifest.json`、`package_quality_report.json` 和 `release_summary.md`。本阶段默认不需要新增 SolidWorks API 证据，也不查几何、OCR 或 PDF 视觉内容。

若发现报告字段缺失、导出格式异常或路径语义无法判断，先读取对应阶段的 `build_report.json`、`drawing_report.json`、`dimension_report.json`、`title_block_report.json` 和 `package_quality_report.json`。只有确认问题来自 V1.1 到 V1.3 的 SolidWorks API 调用时，才回到相应阶段补 API evidence。

## V1.8 零件族 API 证据矩阵

### 目标与适用范围

本节记录三个零件族的 API 证据边界。Schema、Validator、Registry、BuildPlan 和 dry-run 不依赖 SolidWorks API；只有进入独立真实 smoke 前才必须完成对应族的诊断证据。

### 输入与输出

输入为零件族 BuildPlan、已查证官方 API、本地宏录制、诊断报告和返回值。输出为每族独立 `ApiEvidenceReport`，必须包含选定策略、拒绝策略、`failure_stage`、实际返回值、产物路径和回填条件。

| 零件族 | 当前证据 | 允许的开发结论 | 进入真实验收前的缺口 |
|---|---|---|---|
| `plate_basic_4holes` | 最新 `diagnostic_report.json` 记录 `FeatureExtrusion2` 基体拉伸、活动孔草图 `FeatureCut4`、SLDPRT 保存和 STEP 导出均成功 | 保留已有 `SolidWorksPlateFeatureBuilder` 真实能力与回归测试 | V1.8 不改写已验证的切孔参数顺序；若回归失败，回到 plate 诊断 Runner。 |
| `flange_basic` | 几何上可候选复用 `CreateCircle`、`FeatureExtrusion2`、`FeatureCut4` | 证据足以支持 BuildPlan 和 dry-run 语义，不足以声称真实 SolidWorks 验收 | 必须建立 `flange_basic` 独立 smoke 入口，验证同心圆、环形基体、中心孔、螺栓孔阵列、保存和导出的返回值与产物。 |
| `shaft_basic` | 目标为用 `CreateLine`、`CreateCenterLine` 建立截面，再用 `FeatureRevolve2` 旋转 | 证据只足以定义候选 BuildPlan 和 dry-run，未证明真实旋转成功 | 必须有专用诊断 Runner，记录草图封闭状态、中心线构造线状态、`FeatureRevolve2` 完整参数与返回对象，再决定是否回填真实 Builder。 |

### 执行步骤与验证标准

1. 先通过相应零件族的参数 Validator 和 dry-run，确保 API 诊断不承担输入修正职责。
2. 按“官方 API Help → 本地 SDK/宏录制 → 独立诊断报告→只读参考资料”的顺序补齐证据。
3. 诊断 Runner 必须默认关闭，并保留独立安全开关、`failure_stage` 与非空产物校验。
4. 只有独立 smoke 成功且证据可重现，才允许把 API 路径回填对应 `PartFamilyBuilder`，随后仍要通过 Worker、ArtifactValidator、Reviewer 和 QualityGate。

### 常见失败与禁止事项

`flange_build_failed` 必须指向具体的草图、拉伸、切除或阵列步骤；`shaft_build_failed` 必须指向截面、中心线、旋转或台阶映射步骤。证据不足时使用明确的 evidence-insufficient 原因停止，不得凭感觉修改 COM 长参数，不得复制第三方脚本，不得以 dry-run 或文件存在冒充真实验收。

## V1.9 Phase 1 API 证据合同

### 目标、输入与输出

本节把 V1.8 候选策略固定为 V1.9 独立 diagnostic 的唯一候选路径。输入是官方 API、已验证 plate 本地证据、法兰/轴专用诊断日志和产物；输出是每族可回填的 `ApiEvidenceReport` 或 `part_family_api_evidence_insufficient`。

### 法兰策略

1. 外圆：`CreateCircle` 创建外圆，`FeatureExtrusion2` 拉伸为实心圆盘。
2. 中心孔：新建独立活动草图，创建内孔圆，再用 `FeatureCut4` 切除。
3. 螺栓孔：按分布圆计算全部孔中心，在单一活动草图中创建全部圆，再用一次 `FeatureCut4` 切除。

拒绝 `HoleWizard` 和圆周阵列 API，避免引入未验证长参数与选择状态。

### 轴策略

1. `CreateLine` 构建完整闭合轴向轮廓，包含可选台阶。
2. `CreateCenterLine` 创建旋转中心线，并使用 selection mark `16`。
3. `FeatureRevolve2` 执行 360° 旋转，并记录完整参数、轴线选择状态、返回 Feature 和重建结果。

偏移多段拉伸仅作为 backlog / 拒绝策略，本轮不与旋转路径混用。

### 验证、失败与禁止事项

Phase 1 入场时只能声称法兰与轴的证据足以进入独立 diagnostic，两族实际 smoke 结果、运行标识和报告路径尚待回填；该限制现已由下述 Phase 2 API 证据回填关闭。

diagnostic 必须默认关闭，并记录每个 API 的参数单位、草图状态、选择标记、返回值、保存/导出结果和专用 `failure_stage`。证据不足时返回 `part_family_api_evidence_insufficient`，不得盲改 COM 参数、不得直接回填主 Worker、不得用诊断 Runner 作为最终验收入口。

## V1.9 Phase 2 API 证据回填

Phase 1 的待回填状态已经完成。两族 diagnostic 均只标记为 `CandidatePassed`，并由后续 CLI 主工作流程补齐最终可交付证据。

### 法兰证据

- diagnostic：`output/solidworks/diagnostics/v1_9/flange_basic/20260720_081331_449_b538c0c180d44bc6a3007a34e1bd1c0f/evidence_report.json`。
- 产物：SLDPRT 91751 字节，STEP 48876 字节，均非空。
- 审查：同目录 `review/flange_basic_review_report.json` 为 100 分且 `pass`；特征树包含一个 `Extrusion` 和两个 `ICE`，四视图确认中心孔及 6 个螺栓孔。
- 最终主流程：`output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/`，状态为 `Passed`、`Deliverable`、QualityGate `Passed`。
- 最终 `build_report.json` 的 `api_evidence` 已包含 `v1_9_flange_diagnostic_visual_review_and_main_workflow_passed`。

### 轴证据

- diagnostic：`output/solidworks/diagnostics/v1_9/shaft_basic/20260720_081653_181_b0b4a7315e994226b8361ee551be7e6b/evidence_report.json`。
- 产物：SLDPRT 91716 字节，STEP 23323 字节，均非空。
- 审查：同目录 `review/shaft_basic_review_report.json` 为 100 分且 `pass`；特征树包含 `Revolution`，四视图确认直径 40 主体及直径 32、直径 24 两级台阶。
- 最终主流程：`output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/`，状态为 `Passed`、`Deliverable`、QualityGate `Passed`。
- 最终 `build_report.json` 的 `api_evidence` 已包含 `v1_9_shaft_diagnostic_visual_review_and_main_workflow_passed`。

### 证据边界

diagnostic 中的 `geometry_body_count_status` 与 `theoretical_volume_status` 仍为 `NotVerified`。现有专用 API、特征树、四视图、非空产物、主工作流程和质量门禁证据足以完成 V1.9 基础零件族验收；body count 和理论体积自动核验作为非阻断 Improvements 保留。不得仅凭 `CandidatePassed` 跳过主工作流程，也不得据此进入 V2.0。

## V2.0-B 通用 Feature Handler 证据门禁

### 结论与范围

V2.0-B 的五类通用 Handler 均为 `api_evidence_status=unverified`。这表示 Registry、Schema、参数校验和 BuildPlan 已具备，但通用 COM 参数映射尚未获得可重复的独立诊断授权。V1.9 的固定 plate、flange、shaft 路径只证明特定零件族、特定版本和特定参数轮廓，不得自动提升通用 Handler。

| Handler | 状态 | 已有受限证据 | 仍需关闭的缺口 | 详细文档 |
|---|---|---|---|---|
| `sketch` | `unverified` | 固定基准上的直线、圆和部分矩形路径 | 通用基准/面引用、实体全集、约束/尺寸、闭合轮廓证明 | `src/Workers/SolidWorks/Features/Sketch/api_evidence.md` |
| `extrude_boss` | `unverified` | plate / flange 固定盲拉伸 | 通用参数映射、`mid_plane`、返回与重建合同 | `src/Workers/SolidWorks/Features/Extrude/api_evidence.md` |
| `extrude_cut` | `unverified` | plate / flange 固定盲切超深 | `through_all` 与深度映射、通用目标与返回合同 | `src/Workers/SolidWorks/Features/Cut/api_evidence.md` |
| `hole` | `unverified` | 零件族使用草图圆加切除 | 尚无接受的 Hole Wizard 通用映射；放置、方向、终止、标准与类型未决 | `src/Workers/SolidWorks/Features/Hole/api_evidence.md` |
| `revolve_boss` | `unverified` | shaft 固定 360° 旋转与 selection mark `16` | 通用 profile / axis 引用、角度和选择适配 | `src/Workers/SolidWorks/Features/Revolve/api_evidence.md` |

### 证据合同

任何 Handler 提升为 `verified` 前，证据必须至少记录：

- `EvidenceId`、`HandlerVersion` 和 `SourceRevision`。
- SolidWorks 精确版本及运行环境。
- 精确参数轮廓、单位、基准/目标引用和选择标记。
- 使用的官方 API、完整参数语义、返回值和已知失败模式。
- 独立诊断运行路径、日志、重建结果和非空产物。
- 同一参数轮廓的正向、非法输入和 API 失败测试。

证据来源优先级为官方 SolidWorks API Help、本地 SDK 或宏录制、可重复独立诊断、只读参考分析。不得复制第三方代码，也不得将第三方脚本接入生产路径。

### 执行门禁

真实 Worker 必须在 COM 连接前对整张图完成适配、参数校验和 evidence 检查。任一 Handler 不是 `verified` 时返回 `feature_api_evidence_insufficient`，并保持未连接、未执行。不得先执行图中其他节点，也不得用 V1.9 专用 Builder 结果替代通用 Handler 诊断。

`HoleWizard5` 只作为被拒绝的官方研究候选记录；在参数、返回、放置和终止语义未完整验证前，不能作为 Hole Handler 授权。所有候选 API 的官方链接和拒绝原因见各 Handler 的 `api_evidence.md`。

## V2.0-C Adapter 诊断候选证据

### 结论

V2.0-C 将 API 执行从纯逻辑 Handler 移到 `ISolidWorksFeatureAdapter` / `RealSolidWorksFeatureAdapter`。Handler 不得引用 COM。2026-07-30 专用诊断已将 TopPlane 上的 line/rectangle/circle、blind extrude、blind cut 和 `simple_circular_cut_blind` 精确轮廓提升为 `verified`；revolve 与其他未验证能力仍以 `feature_api_unverified` 阻断。

| 能力 | 精确已验证轮廓 | 仍未验证 | 最终状态 |
|---|---|---|---|
| Sketch | TopPlane line、center rectangle、circle；空约束/尺寸；毫米单位 | 任意面、arc、slot、约束/尺寸执行 | `verified`，仅精确 profile |
| Extrude | 单端、正深度 blind、无 draft/thin、合并结果 | `mid_plane`、其他 scope | `verified`，仅精确 profile |
| Cut | 单端、正深度 blind、`through_all=false`、单实体 scope | `through_all`、normal/thin、多实体 scope | `verified`，仅精确 profile |
| Hole | 直径匹配单圆草图加正深度 blind `FeatureCut4`、无 wizard | 原生 `SimpleHole2`、Hole Wizard、其他放置/终止语义 | `verified`，仅精确 profile |

### 证据采集

专用 diagnostic 必须输出 `output/solidworks/features/<timestamp>/model.SLDPRT`、`model.STEP` 和 `feature_execution_report.json`，并记录源码修订、SolidWorks/Adapter/Handler 版本、参数轮廓、单位、引用/选择状态、完整 API 参数、返回对象、重建、失败阶段和文件大小。

旧 run `output/solidworks/features/20260730_073759_9143941/feature_execution_report.json` 的 Cut/Hole 虽有非空对象和成功重建，人工复核却没有孔，因此必须从 verified evidence 中排除。

采用的新 run 是 `output/solidworks/features/20260730_085830_6592380/feature_execution_report.json`。SolidWorks 为 `33.5.0`，Handler/Adapter 为 `2.0-c.2`，复合源码修订为 `feature-execution-source-sha256:71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918`；SLDPRT 为 73416 bytes，SHA256 为 `E411188A101E49EFB1BD835E9BEF3A16EA0EE3BB124A873FFB96EDC5E72012E3`；STEP 为 26403 bytes，SHA256 为 `EF532158373D512CF31A76FE930CD90FBF21A913E52608CF9A04805F0590CE06`。

Boss、Cut、Hole 的体积分别为 `0→5.9999999999999995E-05`、`5.9999999999999995E-05→5.9214601836602546E-05`、`5.9214601836602546E-05→5.84292036732051E-05` m³；7 个 diagnostic 特征报告均完成结果对象、重建和几何变化校验。diagnostic 预览仍需人工复核，最终 100/`pass`、`Extrusion` 加两个 `ICE` 和两个孔的确认以同次 E2E 审查报告为准。

该 run 的原始报告仍是 `CandidatePassed`、`main_workflow_accepted=false`、`quality_gate_passed=false`、`NotDeliverable`。它支持四个精确 profile 的 evidence 提升，但最终交付仍必须从 `run-cad-workflow` 经过完整主工作流程。

生产前，`FeatureHandlerRegistry` 必须在 `ConnectAsync` 前校验每个 Handler 的未知参数、精确 `ParameterProfile`、复合源码修订与 diagnostic 报告绑定；未知参数返回 `invalid_feature_parameter`，证据、profile、修订或 diagnostic 不匹配返回 `feature_api_unverified`。Hole 还必须直接依赖声明的 `sketch_id`，该草图只能有一个圆且圆直径必须等于请求孔径，否则同样在 COM 前返回 `feature_api_unverified`。连接后再以实际 SolidWorks `33.5.0` 逐 Handler 校验运行时版本；不匹配时不得调用建模 API。

最终证据目录为 `output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260730_090151_162-1b16c3982731423a8ae9a93f1db2dbbe/`：E2E Final / QualityGate 为 `Passed`，`all_source_reports_passed=true`、`deliverable_status=Deliverable`，Feature 为 7/7 且结果、重建、几何变化、产物校验均为 `true`。

### 失败与禁止事项

未验证使用 `feature_api_unverified`；API 返回但对象或重建无效使用 `feature_result_invalid`；当次 SLDPRT、STEP 或报告缺失使用 `feature_artifact_missing`。禁止 Handler COM、第三方脚本生产路径、`SimpleHole2` / Hole Wizard、历史产物复用和进入 V2.0-D。

## V2.0-D GeometryReader API 证据边界

V2.0-D 新增的真实几何读取必须收敛在 ISolidWorksGeometryReader / RealSolidWorksGeometryReader。ModelUpdateService、GeometryValidator、报告组装器和 QualityGate 只能处理 DTO，不得读取或写入 COM。每一个新增读取 API 都要有独立的官方证据、最小诊断、运行时版本和源码 revision 绑定；变更 V2.0-C 绑定源码时，必须重新诊断、复核并以主流程刷新证据。

候选读取范围包括：

- 实体：`IPartDoc.GetBodies2`，用于真实实体数量与逐实体测量；
- 包围盒：`IPartDoc.GetPartBox(true)`，仅作近似包围盒检查；
- 体积：`IBody2.GetMassProperties(1d)` 的体积槽位，或 `IModelDocExtension.CreateMassProperty2` / `IMassProperty2.Volume`；
- 质量属性：可用时读取 `IMassProperty2.Mass`，不可用必须明确报告为不可用，不能伪造通过；
- 特征结果：`IFeature.GetTypeName2`、`FirstFeature` / `GetNextFeature` 与 `GetErrorCode2`；
- 精确尺寸：平面 `plate_basic_4holes` 先以 `IBody2.GetVertices` / `IVertex.GetPoint` 读取真实外轮廓顶点；顶点不可读时回退 `IBody2.GetExtremePoint`。孔径或轴径使用有完整证据的圆柱面参数。两条长度路径均不可用时必须返回 `geometry_read_failed`。

GetPartBox 近似结果、COM 非空返回、Feature 名称或文件大小均不能证明精确长度、孔径或四个孔。四孔 plate_basic_4holes 必须读取并验证四个相应的圆柱几何；不得为了凑数新增 Feature 类型、零件族或使用未证实的 pattern、Hole Wizard / SimpleHole2。任何证据不足都应停止为 feature_api_unverified，最终仍只由 run-cad-workflow 的 QualityGate 判定；V2.0-E 不得绕过这条链。

## V2.0-E 现行 FeatureGraph 证据绑定

历史 V2.0-C/D 记录仅保留为审计背景，不能作为当前源码的生产授权。现行四类 Handler 与 V2.0-D 三圆 profile 统一绑定 `evidence/solidworks/20260821_034143_9836278/feature_execution_report.json`：SolidWorks `31.5.0`、源码 revision `feature-execution-source-sha256:1795e60b5855ee1140db9b979d34ae0672490d4f7e384ec820c8acadc9baa211`，报告的 `CandidatePassed` / `NotDeliverable` 语义不变。策略逐项读取诊断、校验受绑定物理 SLDPRT、有效 STEP 内容、版本、特征结果和体积变化；任何失配都在 COM 连接前返回 `feature_api_unverified`。

## V2.1-A 夹套 API 证据

`jacket_basic` 不扩大写入 COM API 面，Builder 使用 `CreateCircle`、`FeatureExtrusion2` 和 `FeatureCut4`，并复用 `RealSolidWorksGeometryReader` 做实体、外包络、圆柱直径和体积实测。FeatureGraph 固定为 TopPlane 外圆盲拉伸和 TopPlane 内圆盲切，切除深度为两倍轴向长度。V1.9 自由文本和历史产物不包含 V2.1-A 的结构化源码修订、运行时版本、diagnostic 与有效 STEP 内容，当前只允许保留实现和 dry-run，真实执行必须在 COM 连接前返回 `part_family_api_evidence_insufficient`。恢复授权必须重新采集受控证据，不得伪造或把文件扩展名当成交换格式证明。

## V2.1-B 孔类型证据补充

### 目标、适用范围与输入输出

本节覆盖 `SimpleHole`、`CounterboreHole`、`CountersinkHole`、`TappedHole`；输入是四类参数、选择、官方资料、SDK、宏与同次诊断，输出是逐类型证据状态和执行门禁。完整 API 名称、来源、参数映射、单位、前置选择、返回、已知失败和宏证据集中于 `src/Workers/SolidWorks/Features/Hole/api_evidence.md`。

### 执行步骤与验证标准

先核对类型与图引用，再按官方孔向导资料研究 `CreateDefinition` / `InitializeHole` / `CreateFeature`，`HoleWizard5` 只作长参数对照。新增显式四类均 `unverified`，SDK 反射不等于 COM 取证，未验证不得真实执行。历史普通孔仅为圆加 blind `FeatureCut4`；任何源码绑定变化必须重新采证，不能改写旧报告修订冒充新证据。

几何孔、`Cosmetic Thread`、`Hole Wizard / Tapped Hole` 与真实建模螺纹必须分别记录。攻丝最低真实语义要求孔向导类型与标准/规格/螺距/深度/底孔读回；仅圆柱几何或装饰螺纹不充分。孔数量、直径、深度、两级沉孔、沉头锥面也须独立测量，缺失即失败。诊断通过后仍需同次主流程质量门禁决定交付。

### 常见失败与禁止事项

标准/尺寸映射、选择、坐标变换、单位、终止、空返回、重建与读回任一失败均保留专用阶段。禁止把 `thread_pitch` 写入槽长、猜数据库枚举、以普通 Cut 冒充攻丝、复制第三方脚本或进入 V2.1-C。历史章节中的 verified 只描述当时源码，当前授权以证据策略和能力矩阵为准。
