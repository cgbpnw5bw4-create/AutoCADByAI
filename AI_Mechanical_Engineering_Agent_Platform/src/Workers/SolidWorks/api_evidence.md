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
