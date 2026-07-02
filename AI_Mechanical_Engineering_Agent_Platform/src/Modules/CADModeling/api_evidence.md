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
