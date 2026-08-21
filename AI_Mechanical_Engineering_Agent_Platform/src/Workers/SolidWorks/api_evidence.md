# SolidWorks Worker API 证据规则

## 查证顺序

1. 官方 `SolidWorks API Help`。
2. 本地 SDK、宏录制和已验证诊断报告。
3. `references/external/solidworks-automation-skill-analysis.md`。
4. 用户手动放入的参考仓库，只能只读分析。

## 封装规则

API 调用必须沉淀到 `SolidWorksPlateFeatureBuilder` 或类似封装。`RealSolidWorksWorker` 不应该直接堆满 dynamic COM 调用。

每次 API 修复都要记录：

- 失败阶段。
- 查证来源。
- 候选 API。
- 选定策略。
- 拒绝策略。
- 验证方式。
- 回填位置。

## 禁止事项

- 不凭感觉改长参数 COM 调用。
- 不复制外部 `scripts` 源码。
- 不把第三方 Python 脚本作为生产路径。
- 不自动执行宏。
- 不绕过 Worker、Validator、Reviewer 和 QualityGate。

## 当前重点

`cut_holes_failed` 必须关注 `CreateCircle`、`CreateCircleByRadius`、`FeatureCut3`、`FeatureCut4`、活动草图状态、草图选择 fallback、单位米制和 `Feature` 返回值。`FeatureExtrusion2` 只用于板件基体拉伸，不再作为切孔候选。

## 切孔草图选择规则

当前主路径以用户第二份“拉伸后再切除”宏为准：`FeatureExtrusion2` 创建基体，后续孔草图保持活动状态并调用 `FeatureCut4` 切孔。只有活动孔草图 `FeatureCut4` 路径失败后，才进入草图引用 fallback。

执行 fallback 切孔候选前不能只依赖残留选择集，也不能只按名称调用 `SelectByID2("SKETCH")`。fallback 稳定策略是先捕获孔草图引用，退出草图后按以下顺序重新选择：

1. 草图 `Feature` 对象。
2. 草图对象。
3. 草图轮廓。
4. 草图区域。
5. 草图线段。
6. `FeatureByName`。
7. `SelectByID2("SKETCH")`。

每个 fallback 切孔候选参数尝试前都必须重新建立选择集。若 `FeatureCut4` 活动草图路径与 fallback 候选仍失败，不能继续盲改参数，必须再次对照录制宏和 diagnostic_report 查差异。

## V1.0-B-REPAIR 最新宏证据

当前以用户第二份“拉伸后再切除”宏为准。`FeatureExtrusion2` 只用于创建板件基体；切孔必须在后续孔草图仍为活动草图时调用 `FeatureCut4`。

稳定调用顺序：

1. 选择可用标准基准面。
2. `InsertSketch True` 进入基体草图。
3. 使用 `CreateCenterRectangle` 创建板件外轮廓。
4. 调用 `FeatureExtrusion2` 生成 12 mm 厚板件基体。
5. 再次选择标准基准面。
6. `InsertSketch True` 进入孔草图。
7. 使用 `CreateCircle` 创建孔圆，参数为圆心和圆上一点，单位为米。
8. 不退出孔草图，直接调用宏录制顺序的 `FeatureCut4`。
9. `FeatureCut4` 返回 `null` 时才进入草图引用 fallback。

禁止继续把一草图内部轮廓拉伸作为当前主策略。该方案只能作为历史尝试记录，不能覆盖本轮宏证据。

## V1.1 工程图 API 证据规则

V1.1 只做基础视图工程图。API 证据优先级仍然是官方 `SolidWorks API Help`、本地 SDK 或宏录制、项目诊断报告、只读参考资料。

## V1.1 官方 API 来源

本轮已查证官方 `SolidWorks API Help`，工程图基础视图链路证据充分：

- `ISldWorks.OpenDoc6`：官方条目 `https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~OpenDoc6.html`。
- `ISldWorks.NewDocument`：官方条目 `https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~NewDocument.html`。
- `ISldWorks.ActivateDoc3`：官方条目 `https://help.solidworks.com/2026/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISldWorks~ActivateDoc3.html`。
- `IDrawingDoc.CreateDrawViewFromModelView3`：官方条目 `https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IDrawingDoc~CreateDrawViewFromModelView3.html`。
- `IModelDocExtension.SaveAs`：官方条目 `https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDocExtension~SaveAs.html`。
- `IExportPdfData`、`ISldWorks.GetExportFileData`、`IExportPdfData.SetSheets`：官方条目 `https://help.solidworks.com/2026/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IExportPdfData.html` 和 `https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.interop.sldworks~SolidWorks.interop.sldworks.IExportPDFData~SetSheets.html`。

当前选定的工程图候选 API：

- `ISldWorks.OpenDoc6`：打开 `plate_basic_4holes.SLDPRT`。
- `ISldWorks.ActivateDoc3`：导出或插入视图前激活目标文档。
- `ISldWorks.NewDocument`：基于 `.drwdot` 模板创建 Drawing 文档。
- `IDrawingDoc.CreateDrawViewFromModelView3`：按模型路径和标准视图名创建 `*Front`、`*Top`、`*Right`、`*Isometric` 基础视图。
- `IModelDocExtension.SaveAs`：保存 `SLDDRW`，并在 Drawing 文档为活动文档时导出 PDF。
- `ISldWorks.GetExportFileData` 与 `IExportPdfData.SetSheets`：可用于 PDF 导出配置；不可用时必须记录 warning 并尝试无 export data 的保存路径。

工程图 API 失败时必须记录：

- 失败阶段。
- 使用的模板路径。
- 源零件绝对路径。
- 创建成功的视图列表。
- `SLDDRW` 和 `PDF` 的保存路径、存在状态和大小。
- 失败 API 名称和返回值。

如果 `CreateDrawViewFromModelView3` 或 PDF 导出失败，先在 `SolidWorksDrawingSmokeRunner` 中复现并生成 `drawing_report.json`，再回填 `SolidWorksDrawingBuilder`。不得直接把宏录制文件作为生产路径。

## V1.2 工程图尺寸 API 证据规则

V1.2 只做已有工程图上的最小基础尺寸标注。当前选择非关联尺寸 API，是因为官方证据能直接支持按坐标和数值创建线性尺寸、直径尺寸，并能生成可审计的 `dimension_report.json`。可关联孔中心选取需要更多 `IView.GetVisibleEntities2`、边线和圆弧绑定证据，本轮不作为生产路径。

## V1.2 官方 API 来源

本轮已查证官方 `SolidWorks API Help`，基础尺寸标注的最小证据充分：

- `IDrawingDoc.CreateLinearDim4`：官方条目 `https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~CreateLinearDim4.html`。用于创建非关联线性尺寸，参数包含点数组、显示值、角度和文本高度。
- `IDrawingDoc.ICreateDiamDim4`：官方条目 `https://help.solidworks.com/2024/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IDrawingDoc~ICreateDiamDim4.html`。用于创建非关联直径尺寸，参数包含尺寸点、圆上近点、圆上远点、法向、文本点、显示值和文本高度。
- `IModelDoc2.AddDimension2`：官方条目 `https://help.solidworks.com/2022/english/api/sldworksapi/solidworks.interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~AddDimension2.html`。该 API 需要预先选择实体，本轮仅作为后续关联尺寸候选证据，不作为当前主路径。
- `IDrawingDoc.InsertModelDimensions`：官方条目 `https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~InsertModelDimensions.html`。该 API 用于插入模型尺寸，本轮拒绝使用，因为用户明确不要自动全尺寸标注。
- `IView.GetVisibleEntities2`：官方条目 `https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IView~GetVisibleEntities2.html`。该 API 可用于后续关联视图几何实体，本轮只记录为未来增强候选。

当前选定的工程图尺寸候选 API：

- `ISldWorks.OpenDoc6`：打开 V1.1 的 `plate_basic_4holes.SLDDRW`。
- `IDrawingDoc.ActivateView`：尝试激活 Front 视图，失败时记录 `drawing_view_activate_failed`。
- `IDrawingDoc.CreateLinearDim4`：添加 160 mm 板长、80 mm 板宽、12 mm 板厚、120 mm 与 40 mm 孔中心距。
- `IDrawingDoc.ICreateDiamDim4`：添加 Φ10 孔径尺寸。
- `IModelDocExtension.SaveAs`：保存 `plate_basic_4holes_dimensioned.SLDDRW`，并导出 `plate_basic_4holes_dimensioned.pdf`。

拒绝策略：

- 不使用 `InsertModelDimensions` 自动导入模型尺寸，避免超出 V1.2 范围。
- 不把宏录制作为生产路径。
- 不复制第三方 `scripts`。
- 不在 API 证据不足时硬做关联孔位尺寸。

尺寸失败时必须记录：

- 失败阶段。
- 源工程图绝对路径。
- 已确认视图列表。
- 每个尺寸的名称、预期毫米值、API 策略、状态和失败阶段。
- `SLDDRW`、`PDF` 和 `dimension_report.json` 的保存路径、存在状态和大小。

如果 `CreateLinearDim4`、`ICreateDiamDim4`、保存或 PDF 导出失败，先在 `SolidWorksDrawingDimensionSmokeRunner` 中复现并生成 `dimension_report.json`，再回填 `SolidWorksDrawingDimensionBuilder`。

## V1.3 工程图标题栏 API 证据规则

V1.3 只做带尺寸工程图上的最小标题栏/图纸属性信息。当前选择文档级自定义属性路径，是因为官方证据能直接支持读取当前 Sheet、读取比例、写入自定义属性、刷新工程图、保存 SLDDRW 和导出 PDF，并能生成可审计的 `title_block_report.json`。复杂标题栏表格、国标模板几何绘制和明细栏不作为本轮生产路径。

## V1.3 官方 API 来源

本轮已查证官方 `SolidWorks API Help`，标题栏基础信息链路的最小证据充分：

- `IModelDocExtension.CustomPropertyManager`：官方条目 `https://help.solidworks.com/2026/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDocExtension~CustomPropertyManager.html`。用于取得文档级或配置级自定义属性管理器。
- `ICustomPropertyManager.Add3`：官方条目 `https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ICustomPropertyManager~Add3.html`。用于添加或按覆盖策略写入自定义属性。
- `ICustomPropertyManager.Set2`：官方条目 `https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ICustomPropertyManager~Set2.html`。用于设置已有自定义属性的值。
- `ICustomPropertyManager.Get6`：官方条目 `https://help.solidworks.com/2018/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ICustomPropertyManager~Get6.html`。用于读取并验证自定义属性值。
- `IDrawingDoc.GetCurrentSheet`：官方条目 `https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IDrawingDoc~GetCurrentSheet.html`。用于取得当前 Drawing Sheet。
- `ISheet.GetProperties2`：官方条目 `https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISheet~GetProperties2.html`。用于读取图纸属性数组，其中包含比例字段。
- `IDrawingDoc.EditTemplate`：官方条目 `https://help.solidworks.com/2019/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IDrawingDoc~EditTemplate.html`。该 API 证明存在编辑模板的官方路径，但 V1.3 不进入复杂模板几何编辑。
- `Entering Title Block Data`：官方帮助 `https://help.solidworks.com/2024/English/solidworks/sldworks/t_titleblock_Entering_Title_Block_Data.htm`。该文档说明工程图模板可包含可编辑标题栏字段，本轮用自定义属性支撑字段值。

当前选定的工程图标题栏候选 API：

- `ISldWorks.OpenDoc6`：打开 V1.2 的 `plate_basic_4holes_dimensioned.SLDDRW`。
- `IDrawingDoc.GetCurrentSheet` 与 `ISheet.GetProperties2`：读取当前 Sheet 和比例；比例不可用时记录为 `auto`。
- `IModelDocExtension.CustomPropertyManager`：获取文档级自定义属性管理器。
- `ICustomPropertyManager.Add3`、`Set2`、`Get6`：写入并验证 `PartName`、`DrawingNumber`、`Material`、`Scale`、`DrawingDate`、`Revision`。
- `IModelDoc2.ForceRebuild3` 或 `EditRebuild3`：刷新标题栏字段引用。
- `IModelDocExtension.SaveAs`：保存 `plate_basic_4holes_title_block.SLDDRW`，并导出 `plate_basic_4holes_title_block.pdf`。

拒绝策略：

- 不绘制复杂国标标题栏模板。
- 不创建 BOM、装配图、明细栏、公差系统、形位公差或表面粗糙度。
- 不把宏录制作为生产路径。
- 不复制第三方 `scripts`。
- 不在 API 证据不足时硬做标题栏表格或模板几何编辑。

标题栏失败时必须记录：

- 失败阶段。
- 源带尺寸工程图绝对路径。
- Sheet 和比例读取状态。
- 每个标题栏属性的名称、值、API 策略、状态和失败阶段。
- `SLDDRW`、`PDF` 和 `title_block_report.json` 的保存路径、存在状态和大小。

如果 `CustomPropertyManager`、`Add3`、`Set2`、`Get6`、`GetCurrentSheet`、`GetProperties2`、保存或 PDF 导出失败，先在 `SolidWorksDrawingTitleBlockSmokeRunner` 中复现并生成 `title_block_report.json`，再回填 `SolidWorksDrawingTitleBlockBuilder`。

## V1.4 工程发布包 API 证据边界

V1.4 只做已有真实输出的发布包收集和最小质量检查，不新增 SolidWorks API 调用，不启动 SolidWorks，不调用 COM，也不读取 PDF 视觉内容。因此本阶段默认不需要新的官方 SolidWorks API 证据。

## V1.7-REAL-AUTH 文档释放 API 证据

API：`ISldWorks.CloseDoc(string Name)`。资料来源：SOLIDWORKS 2025 API Help 的 CloseDoc Method（ISldWorks）；签名为 `void CloseDoc(string Name)`，按文档名称关闭已打开文件，未打开名称无副作用。

本轮仅在每个受控 Worker 阶段完成保存/导出后关闭 `plate_basic_4holes` 的受控零件或工程图名称，以释放发布包读取所需的文件锁。不会调用 `CloseAllDocuments`，避免关闭用户的其他可见文档；若释放失败，只记录 `solidworks_document_close_warning`，后续发布包必须以可行动的 `artifact_copy_failed` 失败，而不能伪造成功。

## V1.7-REAL-AUTH 工程图视图回退 API 证据

API：`IDrawingDoc.CreateDrawViewFromModelView3(string ModelName, string ViewName, double LocX, double LocY, double LocZ)` 与 `IDrawingDoc.Create3rdAngleViews2(string ModelName)`。资料来源：SOLIDWORKS 2025 API Help；前者要求模型完整路径和精确视图名称（标准视图保留 `*`），后者返回布尔值并创建第三角法的三个正交标准视图。

本机 2025 中文环境中，`*Front` 的单视图调用返回空对象时，受控 Builder 仅回退到已验证的 `Create3rdAngleViews2`，然后仍显式创建 `*Isometric`。第三角法或等轴视图任一失败都必须保留对应 `failure_stage`，不能保存空工程图或把 API 回退解释为成功。

本阶段证据来自文件系统和既有阶段报告：`build_report.json`、`diagnostic_report.json`、`drawing_report.json`、`dimension_report.json`、`title_block_report.json`、`release_manifest.json` 和 `package_quality_report.json`。若这些报告显示某个上游阶段的导出或保存 API 失败，必须回到对应 V1.0-B、V1.1、V1.2 或 V1.3 API evidence 流程处理，不在 V1.4 直接修复 CAD API。

## V1.7-REAL-AUTH 标题栏属性回读兼容证据

API：`ICustomPropertyManager.Add3`、`ICustomPropertyManager.Get6` 和 `ICustomPropertyManager.Get`。资料来源：SOLIDWORKS 2025 API Help。`Add3` 负责写入文档级自定义属性；`Get6` 是首选的带六个 by-reference 输出值的读取 API；`Get` 已被官方标记为旧 API，但仍返回指定属性的直接字符串值。

本机真实执行验证：`Add3("PartName", 30, "plate_basic_4holes", 2)` 返回 `0`，紧接着 `Get("PartName")` 返回 `plate_basic_4holes`。在 late-bound .NET COM 反射下，`Get6` 的六个 out/ref 槽可能未回填到调用数组；因此生产路径仍先调用 `Get6`，仅当其回读为空时以 `Get` 进行同一属性、同一值的只读核验。该兼容回退不会把 API 调用成功当作通过：`Add3/Set2` 结果和精确回读值都必须通过，随后仍须经过保存、PDF 导出、Validator、Reviewer 和 QualityGate。

## V1.8 零件族真实 API 证据边界

### 目标与适用范围

本节用于决定 `plate_basic_4holes`、`flange_basic` 和 `shaft_basic` 的 `PartFamilyBuilder` 是否可以进入真实 Worker。证据只支持已验证的几何操作，不会因 API 名称看似可用就授权生产路径。

### 输入与输出

输入必须包含官方 API 条目、完整参数、本地宏录制或 SDK 例程、诊断 Runner 运行日志和返回值。输出为独立 evidence report，至少记录 `part_type`、operation、API、参数策略、成功标志、失败阶段、SLDPRT / STEP 路径与大小。

### 官方 API 来源

- `ISketchManager.InsertSketch`：`https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~InsertSketch.html`
- `ISketchManager.CreateCircle`：`https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateCircle.html`
- `IFeatureManager.FeatureExtrusion2`：`https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureExtrusion2.html`
- `IFeatureManager.FeatureCut4`：`https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureCut4.html`
- `ISketchManager.CreateLine`：`https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateLine.html`
- `ISketchManager.CreateCenterLine`：`https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateCenterLine.html`
- `IFeatureManager.FeatureRevolve2`：`https://help.solidworks.com/2021/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureRevolve2.html`
- 官方整周旋转 C# 示例：`https://help.solidworks.com/2025/English/api/sldworksapi/Create_360-degree_Revolve_Feature_Example_CSharp.htm`

`FeatureExtrusion2` 已被官方标记为旧 API，并指向 `FeatureExtrusion3`。但当前本机 plate 真实链路已验证 `FeatureExtrusion2`，V1.8 不同时做拉伸 API 迁移，避免引入无关回归风险。

### 本地证据来源

- `src/Modules/CADModeling/api_evidence.md`
- `src/Workers/SolidWorks/api_evidence.md`
- `src/Workers/SolidWorks/SolidWorksPlateFeatureBuilder.cs`
- `output/solidworks/diagnostics/plate_basic_4holes/20260702_030612_336_6bb7588d5f4f4f10a426bc6d33fc9160/diagnostic_report.json`
- `references/external/solidworks-automation-skill-analysis.md`

历史 `cut_holes_failed_api_evidence_report.json` 中的“使用 `FeatureExtrusion2` 切孔”结论已被后续宏证据和真实成功诊断推翻，不得回填 V1.8。本轮没有使用第三方脚本作为 API 来源。

### `plate_basic_4holes` 证据

最新可用 `diagnostic_report.json` 在 SolidWorks `33.5.0` 上记录了连接、新建零件、`FeatureExtrusion2` 基体拉伸、活动孔草图 `FeatureCut4`、SLDPRT 保存和 STEP 导出成功，最终状态为 `Passed`。V1.8 应复用 `SolidWorksPlateFeatureBuilder`，不重新猜测切孔长参数，并用回归测试保护该真实能力。

### `flange_basic` 候选证据

法兰最小几何可候选复用：

1. 选择标准基准面并进入草图。
2. 用 `CreateCircle` 创建外圆，再复用已验证的 `FeatureExtrusion2` 参数创建圆盘。
3. 在新的活动草图中创建中心孔和螺栓孔。螺栓孔中心按 `x = BCD / 2 * cos(angle)`、`y = BCD / 2 * sin(angle)` 展开，毫米输入在调用前转为米。
4. 复用已验证的活动草图 `FeatureCut4` 参数，一次切除全部圆形轮廓。
5. 进入既有保存、STEP 导出、ArtifactValidator 和 QualityGate 链路。

上述组合以“官方闭合圆轮廓能力 + 本机已验证各组成调用”为依据，属于低风险推断，证据足以设计并进入默认关闭的独立 flange smoke 入口开发，但当前尚无该 Runner，也未完成真实验收。首次真实执行必须使用独立 flange smoke Runner，分步记录草图完整性、每个 API 返回值、螺栓孔数量、保存/导出结果和 `flange_build_failed` 的子阶段。在该 smoke 通过前，只能声称 flange dry-run 通过，不得接入默认真实主路径。

### `shaft_basic` 候选证据

无台阶圆柱可以复用 `CreateCircle` 和 `FeatureExtrusion2`，组成 API 证据充分；但完整 `shaft_basic` 还包含可选台阶，本轮不能只验收无台阶特例就声称整族真实能力完成。可选台阶的候选策略是：

1. 用 `CreateLine` 构建半截面外轮廓和可选台阶轮廓。
2. 用 `CreateCenterLine` 创建旋转轴线，并确认其为构造线。
3. 用 `FeatureRevolve2` 执行整周旋转。

当前未有足以支持生产回填的专用旋转诊断报告。进入真实验收前必须用 shaft 专用 Runner 验证草图封闭、台阶数组到轮廓的映射、中心线选择状态、`FeatureRevolve2` 完整参数和返回 Feature，并生成非空 SLDPRT / STEP。在证据齐全前，`shaft_basic` 只能运行 dry-run。

### 拒绝策略

- 不使用 `HoleWizard`，避免超出简单通孔范围并增加长参数风险。
- 法兰螺栓孔不使用圆周阵列 API；直接计算孔中心并在一个草图中创建全部圆，可减少选择状态。
- V1.8 不迁移到 `FeatureExtrusion3`，避免对 plate 已验证路径引入无关改动。
- 轴不使用 `SelectByRay` 或逐个端面拉伸来生成台阶，因为端面选择脆弱，也难以稳定覆盖任意台阶序列。
- 不把官方 `FeatureRevolve2` 示例直接复制为生产实现，必须先经专用诊断 Runner 验证。

### 执行步骤、验证标准与禁止事项

1. 先运行零件族 Validator 和 dry-run；输入非法时不得进入 API 诊断。
2. 按官方 API、本地 SDK/宏录制、专用 Runner、只读参考资料顺序形成 evidence。
3. 真实 smoke 必须默认关闭，显式开启后仍要经过 ArtifactValidator、Reviewer 和 QualityGate。
4. 仅当专用 Runner 成功、报告可复现且产物非空时，才可将 API 路径回填该族 Builder。

禁止用 plate 的 API 成功报告代替 flange 或 shaft 专用证据，禁止在 evidence 不足时盲改 COM 长参数，禁止自动执行宏或复制第三方脚本，禁止把文件存在当作真实几何成功。

## V1.9 Phase 1 真实 Builder API 证据

### 目标与当前状态

本节定义 flange 和 shaft 从候选 API 进入独立 diagnostic 的严格路径。官方 URL 沿用上文 `CreateCircle`、`FeatureExtrusion2`、`FeatureCut4`、`CreateLine`、`CreateCenterLine`、`FeatureRevolve2` 和官方整周旋转示例。

Phase 1 入场结论仅为：两族证据足以进入默认关闭的独立 diagnostic，flange 和 shaft 的实际 smoke `final_status`、`run_id` 和报告路径尚待回填；该限制现已由下述 Phase 2 真实证据关闭。

### flange 选定策略

1. 选择标准基准面，进入外圆草图。
2. `CreateCircle` 创建外圆，`FeatureExtrusion2` 生成实心圆盘。
3. 在新的独立活动草图中创建中心孔，用 `FeatureCut4` 切除。
4. 新建螺栓孔草图，按分布圆计算全部孔中心，在同一草图中创建全部圆。
5. 在螺栓孔草图仍活动时调用 `FeatureCut4`，一次切除全部螺栓孔。

该策略严格拒绝 `HoleWizard` 和圆周阵列 API。失败分别记录 `flange_profile_create_failed`、`flange_extrude_failed`、`flange_inner_cut_failed` 或 `flange_bolt_holes_failed`。

### shaft 选定策略

1. `CreateLine` 创建包含基础直径和可选台阶的闭合轴向半截面。
2. `CreateCenterLine` 创建旋转轴线，将其设为构造线，并以 selection `mark=16` 选中。
3. `FeatureRevolve2` 执行 360° 旋转，验证返回 Feature、重建结果和台阶几何。

失败分别记录 `shaft_profile_create_failed`、`shaft_revolve_failed` 或 `shaft_step_feature_failed`。偏移多段拉伸保留在 backlog / 拒绝策略中，V1.9 Phase 1 不与旋转路径混用。

### 输入输出、验证和回填条件

diagnostic 输入包含零件族参数、模板、输出目录和显式安全开关。输出必须包含官方 API 来源、每步参数和单位、草图/选择状态、返回值、子阶段失败、SLDPRT / STEP 路径和文件大小。

只有独立 Runner 输出可重现 `Passed`、产物非空、人工打开几何检查通过，才能把证据回填真实 Builder。回填后仍必须经完整主工作流程验收，诊断成功不等于最终可交付。

证据不足时返回 `part_family_api_evidence_insufficient`。禁止盲改长参数、复制官方示例为生产代码、自动执行宏、使用第三方脚本，或以 Builder / SmokeRunner 代替最终主工作流程。

## V1.9 Phase 2 真实证据结果

### diagnostic 与视觉复核

`flange_basic` 专用 diagnostic 位于：

```text
output/solidworks/diagnostics/v1_9/flange_basic/20260720_081331_449_b538c0c180d44bc6a3007a34e1bd1c0f/
```

其 `evidence_report.json` 为 `CandidatePassed`。SLDPRT 为 91751 字节，STEP 为 48876 字节；`review/flange_basic_review_report.json` 规则评分为 100 且通过。特征树显示一个 `Extrusion` 与两个 `ICE`，四视图人工检查确认中心孔和 6 个螺栓孔，符合外圆拉伸、中心孔独立切除、螺栓孔单草图切除策略。

`shaft_basic` 专用 diagnostic 位于：

```text
output/solidworks/diagnostics/v1_9/shaft_basic/20260720_081653_181_b0b4a7315e994226b8361ee551be7e6b/
```

其 `evidence_report.json` 为 `CandidatePassed`。SLDPRT 为 91716 字节，STEP 为 23323 字节；`review/shaft_basic_review_report.json` 规则评分为 100 且通过。特征树显示 `Revolution`，四视图人工检查确认直径 40 主体和直径 32、直径 24 两级台阶，符合闭合轮廓、中心线及 `FeatureRevolve2` 路径。

### 最终主工作流程证据

最终 metadata 回填运行如下：

```text
output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/
output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/
```

两次运行的 `e2e_execution_report.json` 均为 `Passed`、`Deliverable`、QualityGate `Passed`，真实 Worker、连接、执行和同次源报告证据均通过。最终 `build_report.json` 分别记录 `v1_9_flange_diagnostic_visual_review_and_main_workflow_passed` 与 `v1_9_shaft_diagnostic_visual_review_and_main_workflow_passed`。此前两条成功 CLI 路径只作为 metadata 回填前的过程记录，不是最终交付路径。

### 修复证据与限制

flange 首次 diagnostic 的 `part_save_failed` 通过复用 plate 已验证的 `SaveAs3` 主路径和 `SaveAs` 回退路径修复。首次主流程的瞬时源文件哈希读锁曾导致 `artifact_copy_failed`；修复后仍先要求复制与目标校验成功，再把已经恢复的源读锁降级为 warning，最终重跑通过。

两族 diagnostic 的 body count 和 theoretical volume 字段仍为 `NotVerified`。它们不削弱本阶段已经形成的特征树、四视图、非空产物和完整主流程证据，但自动核验仍应作为 Improvements 实现。不得放宽目标文件校验，不得把 warning 当作任意复制错误的豁免，也不得进入 V2.0。

## V2.0-C Feature Adapter API 证据

### 架构证据边界

`ISolidWorksFeatureAdapter` 定义可测试的 API 边界，`RealSolidWorksFeatureAdapter` 是唯一允许访问 SolidWorks COM 的通用特征实现。Handler 只保留纯逻辑 Schema、校验和命令，不得引用 Interop。V1.9 专用 Builder 的成功证据可帮助筛选候选，但不自动验证 Adapter。

### 四类最小候选

| 能力 | 官方候选 | 参数轮廓 | 当前状态 |
|---|---|---|---|
| line | [`ISketchManager.CreateLine`](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateLine.html) | TopPlane、两个端点、米制坐标 | `verified`，仅精确轮廓 |
| rectangle | [`ISketchManager.CreateCenterRectangle` 所在官方方法索引](https://help.solidworks.com/2026/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.isketchmanager_methods.html) | TopPlane、中心与宽高、米制坐标 | `verified`，仅精确轮廓 |
| circle | [`ISketchManager.CreateCircle`](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateCircle.html) | TopPlane、圆心与半径、米制坐标 | `verified`，仅精确轮廓 |
| blind extrude | [`IFeatureManager.FeatureExtrusion2`](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureExtrusion2.html) | 正深度、blind、闭合草图 | `verified`，仅精确轮廓 |
| blind cut | [`IFeatureManager.FeatureCut4`](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureCut4.html) | 正深度、blind、单圆草图 | `verified`，仅精确轮廓 |
| hole 组合 | `CreateCircle` + blind `FeatureCut4` | 直径匹配的独立圆草图、正切割深度 | `verified`，仅 `simple_circular_cut_blind` |

孔组合明确拒绝 `SimpleHole2` 和 Hole Wizard；这些名称不得出现在选定 API、实现调用或通过证据中。内部 operation `CreateSimpleHole` 只描述组合语义，不构成调用同名或近名 SolidWorks API 的授权；历史 `AddHoleWizardHole` 标签已退出当前适配映射。

### 诊断和提升条件

专用 diagnostic 必须绑定源码修订、SolidWorks/Adapter/Handler 版本和精确参数轮廓，并输出：

```text
output/solidworks/features/<timestamp>/model.SLDPRT
output/solidworks/features/<timestamp>/model.STEP
output/solidworks/features/<timestamp>/feature_execution_report.json
```

旧 run `output/solidworks/features/20260730_073759_9143941/feature_execution_report.json` 的 Cut/Hole 即使返回非空 Feature 且重建通过，人工复核仍未见孔，因此该 run 不得保留为 verified。

权威诊断报告为 `output/solidworks/features/20260730_085830_6592380/feature_execution_report.json`，SolidWorks 版本 `33.5.0`，Handler/Adapter 版本 `2.0-c.2`，复合源码修订为 `feature-execution-source-sha256:71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918`。SLDPRT 为 73416 bytes，SHA256 `E411188A101E49EFB1BD835E9BEF3A16EA0EE3BB124A873FFB96EDC5E72012E3`；STEP 为 26403 bytes，SHA256 `EF532158373D512CF31A76FE930CD90FBF21A913E52608CF9A04805F0590CE06`。

Boss 体积从 `0` 增至 `5.9999999999999995E-05` m³；Cut 降至 `5.9214601836602546E-05` m³；Hole 再降至 `5.84292036732051E-05` m³。7 个 diagnostic 特征报告的结果对象、重建和几何变化均已校验；最终 100/`pass`、`Extrusion` 加两个 `ICE` 和人工双孔确认以同次 E2E 审查报告为准。

诊断后只把代码中声明的四个精确 `ParameterProfile` 提升为 `verified`。`through_all`、`mid_plane`、原生 `SimpleHole2` / Hole Wizard、任意面及任何其他轮廓仍以 `feature_api_unverified` 阻断。

特征错误校验优先调用官方 `IFeature.GetErrorCode2(out bool IsWarning)`。若晚绑定 RCW 无法封送该 by-ref 布尔参数，允许回退到官方标记为 superseded 但仍保留的无引用参数兼容成员 `IFeature.GetErrorCode()`；两者都不可调用时必须返回 `feature_result_invalid`，不得只依赖重建布尔值。

诊断报告自身仍为 `CandidatePassed`、`main_workflow_accepted=false`、`quality_gate_passed=false`、`NotDeliverable`。诊断通过不等于最终验收；它只提供四个精确 profile 的 evidence。

生产预检必须在 `ConnectAsync` 前校验未知参数、精确 profile、复合源码修订与 diagnostic 绑定；Hole 还必须直接依赖只含一个等直径圆的声明草图。未知参数返回 `invalid_feature_parameter`，其他上述不匹配返回 `feature_api_unverified`。连接后必须以实际 SolidWorks `33.5.0` 复核各 Handler evidence，版本不匹配时不得调用建模 API。

最终主流程已由 `output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260730_090151_162-1b16c3982731423a8ae9a93f1db2dbbe/` 关闭：Final 与 QualityGate 均为 `Passed`，`all_source_reports_passed=true`，`deliverable_status=Deliverable`。Feature 报告为 7/7，结果对象、重建、几何变化和产物校验全部为 `true`，三段体积与 diagnostic 相同。

同次 SLDPRT 为 73830 bytes，SHA256 `045A5CF2C445F66B1CE2502065F84805A40CF217600DAD213C68D9EEFBA78612`；STEP 为 26399 bytes，SHA256 `DCAB84553885D804AB961E4BBABB691A7A47C6962D030781211B7B777E1EF7BB`。`review_active_source/feature_pipeline_plate_review_report.json` 为 100 分、`pass`，特征树为一个 `Extrusion` 加两个 `ICE`，人工确认两个孔。该结论只覆盖已列出的四个精确 profile，不授权其他轮廓。

### 禁止事项

禁止 Handler COM、盲改长参数、未验证生产执行、第三方脚本生产路径、`SimpleHole2` / Hole Wizard、历史产物补证、空 Feature/文件成功和进入 V2.0-D。

## V2.0-D 真实 GeometryReader API 证据

V2.0-D 将 COM 读取限定在 ISolidWorksGeometryReader / RealSolidWorksGeometryReader。ModelUpdateService、GeometryValidator、QualityGate 和 JSON 报告不保留 COM 对象或 RCW。每项新读取 API 必须在实际 SolidWorks 版本上保留官方参考、最小诊断、原始读数、失败阶段和源码 revision；若 V2.0-C 绑定源码被改动，旧 evidence 不能自动沿用，必须重新诊断、审查并通过主流程。

受证前的候选读取策略为：

| 目标 | 候选 COM 读取 | 证据限制 |
|---|---|---|
| Body 数量 | IPartDoc.GetBodies2 | 记录当前受控模型的 Body 集合，不读取历史文件。 |
| BoundingBox | IPartDoc.GetPartBox(true) | 仅近似 BoundingBox；不能用于精确 length_mm 结论。 |
| Volume / Mass | IBody2.GetMassProperties(1d)，或 CreateMassProperty2 / IMassProperty2 | 逐项写出可用性、单位和读取失败，不可伪造 Mass。 |
| Feature 结果 | FirstFeature / GetNextFeature / IFeature.GetTypeName2 / GetErrorCode2 | 同时验证 Sketch、Extrude、Cut、Hole 的真实存在与错误状态。 |
| 长度 / 孔径 | `IBody2.GetVertices` + `IVertex.GetPoint`、`IBody2.GetExtremePoint` 回退、圆柱面参数 | 平面四孔板先以真实 B-rep 顶点计算外包络；顶点不可读时才回退 `GetExtremePoint`。两者都不可用必须失败，不能用近似 BoundingBox 代替。 |

IPartDoc.GetPartBox、文件存在、COM 返回非空、特征名称或 hole_count 都不是实际四孔或尺寸变化的充分证据。plate_basic_4holes 只能复用既有已经注册和取证的 Feature 类型；不要以未证实的 pattern、任意面、through_all、mid_plane、SimpleHole2 或 Hole Wizard 补足结果。证据不足使用 feature_api_unverified，最终交付只由 run-cad-workflow 的 QualityGate 决定；V2.0-E 不得绕过这条链。

`GetVertices` / `GetPoint` 只作为平面四孔板的精确外包络来源：该族的四个外轮廓角点给出实际 `length_mm`、`width_mm` 和 `thickness_mm`。这不是曲面外形的通用替代；对于无足够顶点的零件，Reader 必须取得 `GetExtremePoint` 的完整结果，否则返回 `geometry_read_failed`。实际特征树按 `GetTypeName2` 的 API 类型名判断：`ProfileFeature` 为草图、`Extrusion` 为凸台拉伸、`ICE` 为盲切除；不得依赖本地化显示名称。

### V2.0-D 主流程运行证据

`run-cad-workflow` 在 SolidWorks `33.5.0` 的同次受控运行中完成基线 `160 x 80 x 12 mm` 与更新后的 `200 x 100 x 15 mm` 四孔板。更新报告证明 FeatureGraph 保持不变，且只重执行既有 `plate_boss`、`plate_cut`、`plate_hole`。

更新后的真实测量为：一个实体、外包络 `200 x 100 x 15 mm`、四个平面法向圆柱面、每孔直径约 `10 mm`、实体体积与质量属性体积均为约 `295287.6110196154 mm³`。`geometry_validation_report.json` 的 `final_status=Passed`，同次 `rebuild_report.json`、SLDPRT、STEP、E2E 和 package QualityGate 均为 `Passed` / `Deliverable`。该记录只证明当前四孔板参数更新轮廓，不扩大 V2.0-C 的其他 Feature profile；V2.0-E 仍须保持统一 FeatureGraph 和证据门禁。
## V2.0-D 三圆 blind cut 的独立授权

`V20DThreeCircleCutEvidencePolicy` 不是新的 CAD Feature 或 COM 调用点。它在 `FeatureHandlerRegistry` 通过后、`ConnectAsync` 前对已编译 `SolidWorksBuildPlan` 和受绑定 JSON 证据做纯读取校验，补足 V2.0-C 单圆 `extrude_cut` profile 不覆盖的三圆 `cut_profile`。

固定候选诊断 `evidence/solidworks/20260821_034143_9836278/feature_execution_report.json` 绑定 `examples/parameter_update_plate.json`，在 SolidWorks `31.5.0` 中真实应用 200×100×15 的参数更新，记录 `plate_cut` 从 `0.00030000000000000003` 下降到 `0.00029646570826471146` m³，随后 `plate_hole` 再下降到 `0.00029528761101961536` m³。策略同时校验候选报告哈希、实际 SLDPRT/STEP 物理文件及其大小、STEP 内容、V2.0-C Feature 源码 revision、三圆/第四孔的精确图形和 20 mm 边距映射。

候选诊断只能证明该精确 profile 的 API 行为，不能代替 `run-cad-workflow`、GeometryValidator 或 QualityGate。任何不匹配都以 `feature_api_unverified` 在连接前拒绝，绝不通过修改 Handler、直接 Builder 或伪造文件存在绕过。

## V2.0-E 现行 FeatureGraph 证据绑定

历史 V2.0-C/D 记录仅保留为审计背景，不能作为当前源码的生产授权。现行四类 Handler 与 V2.0-D 三圆 profile 统一绑定 `evidence/solidworks/20260821_034143_9836278/feature_execution_report.json`：SolidWorks `31.5.0`、源码 revision `feature-execution-source-sha256:1795e60b5855ee1140db9b979d34ae0672490d4f7e384ec820c8acadc9baa211`，报告的 `CandidatePassed` / `NotDeliverable` 语义不变。策略逐项读取诊断、校验受绑定物理 SLDPRT、有效 STEP 内容、版本、特征结果和体积变化；任何失配都在 COM 连接前返回 `feature_api_unverified`。

## V2.1-A 夹套 API 证据

夹套 Builder 使用 V1.9 法兰路径中的 `CreateCircle`、`FeatureExtrusion2`、`FeatureCut4` 调用形状，但该历史自由文本不足以授权 V2.1-A 生产执行。当前实现仅覆盖 TopPlane 单外圆盲拉伸、TopPlane 单内圆盲切两倍轴向长度，并在保存前复用 `RealSolidWorksGeometryReader` 校验单实体、外包络、内外圆柱直径和理论体积。真实执行保持连接前失败关闭，直到重新采集与当前源码修订和实际 SolidWorks 版本绑定的 diagnostic，并证明落盘 STEP 以 `ISO-10303-21;` 开始且含完整结束标记；最终证据仍必须来自 `examples/real_cad_jacket_request.json` 的同次主流程与 QualityGate。
