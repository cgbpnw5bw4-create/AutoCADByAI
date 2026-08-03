# SolidWorks Worker 失败修复说明

## 连接失败

检查 `SW_DISABLE_REAL_EXECUTION`、CI/单元测试标记、COM 注册、SolidWorks 是否安装和 `SolidWorksSessionManager` 日志。self-check 不要求连接成功，也不尝试连接。

## 新建 Part 失败

检查模板路径、`SW_TEMPLATE_PART_PATH`、`active_doc_title` 和 `new_part_failed` 详细错误。

## 基准面选择失败

读取 `available_reference_planes`、`selected_plane_strategy` 和 `plane_selection_errors`。优先修 `SolidWorksPlaneSelector`，不要写死 `Top Plane`。

## 草图失败

检查是否已进入草图、草图 API 返回值、坐标单位和基准面选择结果。必要时生成 API evidence。

## 拉伸失败

检查 `FeatureExtrusion` 参数、实体是否生成、方向和深度单位。先在 `SolidWorksSmokeRunner` 中验证。

## 切孔失败

不能直接瞎改 `FeatureCut` 参数。必须：

1. 读取最新 `diagnostic_report.json`。
2. 生成 `ApiEvidenceReport`。
3. 查官方 API。
4. 读取本地分析文档。
5. 如仍不明确，要求用户提供宏录制。
6. 在 `SolidWorksSmokeRunner` 中最多做有限 repair attempt。
7. Runner 成功后再回填 `RealSolidWorksWorker`。

当前切孔修复的首选策略是活动孔草图上的宏录制 `FeatureCut4` 路径，而不是继续增加 `FeatureExtrusion2` 切孔候选。孔草图创建后必须捕获草图引用；只有活动草图 `FeatureCut4` 失败后，才退出草图并优先选择草图 `Feature`、草图对象、轮廓、区域或线段；最后才回退 `FeatureByName` 和 `SelectByID2("SKETCH")`。每个 fallback 切孔候选前都要重新选择草图。

如果活动孔草图 `FeatureCut4` 与稳定草图引用 fallback 候选仍全部失败，说明当前 API 证据不足，应生成 `api_evidence_insufficient`，并要求用户提供最小宏录制结果后再继续修复。

### V1.0-B-REPAIR 最新切孔规则

用户第二份宏已经确认：`FeatureExtrusion2` 是基体拉伸，不是切孔 API。当前修复必须按以下顺序验证：

1. 先通过 `CreateBasePlate` 调用 `FeatureExtrusion2` 生成板件基体。
2. 再选择标准基准面并进入孔草图。
3. 用 `CreateCircle` 创建孔圆。
4. 保持孔草图活动状态，不先退出草图。
5. 调用宏录制参数顺序的 `FeatureCut4`。
6. `FeatureCut4` 返回 `null` 或抛出 COM 异常时，记录候选名、活动草图状态和参数数量。
7. 只有活动孔草图路径失败后，才回退到草图引用选择 fallback。

如果该路径仍失败，应要求用户提供可复制的文本宏，尤其是完整 `FeatureCut4(...)` 参数尾部；不要继续凭截图猜长参数。

## 保存失败

检查 `sldprt_save_*` 字段、输出路径、权限和 `SaveAs` 返回值。不能在 SLDPRT 缺失时返回 Passed。

## STEP 导出失败

检查 `ActiveDoc`、`ActivateDoc`、`ClearSelection2`、`step_export_*` 字段和 STEP 文件大小。导出前必须确保目标文档为活动文档。

## 报告失败

如果 `build_report.json` 或 `diagnostic_report.json` 未写出，先修输出路径和权限，不要继续 CAD API 调试。

## V1.1 工程图基础视图失败

V1.1 工程图失败必须先读取 `drawing_report.json`，再判断 `failure_stage`。默认 self-check 不应启动 SolidWorks；只有工程图 smoke test 明确开启时才允许真实调用。

| failure_stage | 可能原因 | 首先读取 | 处理方式 |
|---|---|---|---|
| `source_part_missing` | 输入的 `plate_basic_4holes.SLDPRT` 不存在或路径错误 | `drawing_report.json`、self-check 的 `real_drawing_output_directory` | 先确认 V1.0-B 是否生成真实零件，再重新传入绝对路径 |
| `drawing_template_missing` | `SW_TEMPLATE_DRAWING_PATH` 未设置且无法在 SolidWorks 模板目录找到 `.drwdot` | `drawing_report.json`、环境变量 | 要求用户提供工程图模板路径，不能盲建空 Drawing |
| `drawing_document_create_failed` | `NewDocument` 返回空或模板不兼容 | `drawing_report.json`、SolidWorks 日志 | 在 `SolidWorksDrawingSmokeRunner` 中隔离验证模板 |
| `source_part_open_failed` | `OpenDoc6` 打开零件失败 | `drawing_report.json` | 检查 SLDPRT 是否完整、是否被占用、路径是否为绝对路径 |
| `source_part_activate_failed` | `ActivateDoc3` 无法激活零件文档 | `drawing_report.json` | 检查活动文档标题和打开文档列表 |
| `front_view_create_failed` | Front 视图创建失败 | `drawing_report.json`、API evidence | 查证 `CreateDrawViewFromModelView3` 的视图名和坐标 |
| `top_view_create_failed` | Top 视图创建失败 | `drawing_report.json`、API evidence | 查证标准视图名 `*Top` 是否适配当前版本 |
| `right_view_create_failed` | Right 视图创建失败 | `drawing_report.json`、API evidence | 查证标准视图名 `*Right` 和模型路径 |
| `isometric_view_create_failed` | Isometric 视图创建失败 | `drawing_report.json`、API evidence | 查证标准视图名 `*Isometric` |
| `slddrw_save_failed` | 工程图保存失败、路径权限不足或 `SaveAs` 返回失败 | `drawing_report.json` | 检查 `slddrw_path`、文件大小和保存错误 |
| `pdf_export_failed` | PDF 导出失败或导出后文件为空 | `drawing_report.json`、API evidence | 确认 Drawing 是活动文档，优先查证 `IModelDocExtension.SaveAs` 和 PDF export data |
| `drawing_report_write_failed` | 报告写出失败 | 输出目录 | 先修目录和权限，不继续改 SolidWorks API |

工程图 API 不确定时必须进入 API evidence 流程，不允许凭感觉修改长参数或把宏作为生产路径。

## V1.2 工程图基础尺寸失败

V1.2 尺寸标注失败必须先读取 `dimension_report.json`，再判断 `failure_stage`。默认 self-check 不应启动 SolidWorks；只有尺寸 smoke test 明确开启时才允许真实调用。

| failure_stage | 可能原因 | 首先读取 | 处理方式 |
|---|---|---|---|
| `source_drawing_missing` | V1.1 的 `plate_basic_4holes.SLDDRW` 不存在或路径错误 | `dimension_report.json`、self-check 的 `real_drawing_dimension_output_directory` | 先确认 V1.1 是否生成真实工程图，再重新传入绝对路径 |
| `source_drawing_open_failed` | `OpenDoc6` 打开工程图失败或无法激活 Drawing | `dimension_report.json` | 检查 SLDDRW 是否完整、是否被占用、路径是否为绝对路径 |
| `drawing_view_missing` | 未确认 Front、Top、Right、Isometric 四个视图 | `dimension_report.json` 的 `views_confirmed` | 回到 V1.1 修复视图创建链路，不在 V1.2 补造视图 |
| `drawing_view_activate_failed` | `ActivateView` 或视图选择失败 | `dimension_report.json`、视图名称 | 查证视图名称读取方式，必要时补充 `IView` 证据 |
| `length_dimension_failed` | 160 mm 长度尺寸创建失败 | `dimension_report.json`、API evidence | 查证 `IDrawingDoc.CreateLinearDim4` 参数、点坐标、单位米制 |
| `width_dimension_failed` | 80 mm 宽度尺寸创建失败 | `dimension_report.json`、API evidence | 查证 `CreateLinearDim4` 垂直尺寸角度和文本点 |
| `thickness_dimension_failed` | 12 mm 厚度尺寸创建失败 | `dimension_report.json`、API evidence | 查证右视图附近的非关联线性尺寸点位 |
| `hole_diameter_dimension_failed` | Φ10 孔径尺寸创建失败 | `dimension_report.json`、API evidence | 查证 `IDrawingDoc.ICreateDiamDim4` 点位、法向和文本点 |
| `hole_position_dimension_failed` | 120 mm 或 40 mm 孔中心距尺寸创建失败 | `dimension_report.json`、API evidence | 先修 `CreateLinearDim4` 非关联中心距；关联孔中心选取留到后续阶段 |
| `dimension_save_failed` | 带尺寸 SLDDRW 保存失败 | `dimension_report.json` | 检查 `slddrw_path`、文件大小、输出路径和权限 |
| `dimension_pdf_export_failed` | 带尺寸 PDF 导出失败 | `dimension_report.json` | 确认 Drawing 是活动文档，检查 PDF export data 和 `SaveAs` 返回值 |
| `dimension_report_write_failed` | 尺寸报告写出失败 | 输出目录 | 先修目录和权限，不继续调 SolidWorks API |
| `drawing_dimension_api_evidence_insufficient` | 当前尺寸 API 证据不足 | `dimension_report.json`、官方 API Help、宏录制 | 停止盲改，补充官方证据或最小宏录制后再继续 |

V1.2 只允许最小尺寸标注。不得借修复失败扩展到 BOM、标题栏、国标模板美化、自动全尺寸标注、复杂公差、表面粗糙度、装配图、钣金展开图或 V1.3。

## V1.3 工程图标题栏基础信息失败

V1.3 标题栏失败必须先读取 `title_block_report.json`，再判断 `failure_stage`。默认 self-check 不应启动 SolidWorks；只有标题栏 smoke test 明确开启时才允许真实调用。

| failure_stage | 可能原因 | 首先读取 | 处理方式 |
|---|---|---|---|
| `source_dimensioned_drawing_missing` | V1.2 的 `plate_basic_4holes_dimensioned.SLDDRW` 不存在或路径错误 | `title_block_report.json`、self-check 的 `real_drawing_title_block_output_directory` | 先确认 V1.2 是否生成带尺寸工程图，再重新传入绝对路径 |
| `source_drawing_open_failed` | `OpenDoc6` 打开带尺寸工程图失败或无法激活 Drawing | `title_block_report.json` | 检查 SLDDRW 是否完整、是否被占用、路径是否为绝对路径 |
| `title_block_template_missing` | 当前工程图没有可用 Sheet 或标题栏模板/Sheet Format 无法识别 | `title_block_report.json`、当前 Sheet 信息 | 先确认工程图 Sheet 可读；不要在 V1.3 临时绘制复杂模板 |
| `custom_property_write_failed` | `CustomPropertyManager`、`Add3` 或 `Set2` 写入失败 | `title_block_report.json` 的 `properties` | 查证自定义属性 API，确认属性名、类型和覆盖策略 |
| `drawing_property_read_failed` | `GetCurrentSheet` 或 `GetProperties2` 无法读取比例/图纸属性 | `title_block_report.json` | 检查当前 Sheet 是否存在，比例不可读时可记录为 `auto` |
| `title_block_update_failed` | 写入属性后工程图刷新或重建失败 | `title_block_report.json`、操作列表 | 查证 `ForceRebuild3` 或 `EditRebuild3`，不要改成宏生产路径 |
| `title_block_save_failed` | 带标题栏信息 SLDDRW 保存失败 | `title_block_report.json` | 检查 `slddrw_path`、文件大小、输出路径和权限 |
| `title_block_pdf_export_failed` | PDF 导出失败或导出后文件为空 | `title_block_report.json` | 确认 Drawing 是活动文档，检查 PDF export data 和 `SaveAs` 返回值 |
| `title_block_report_write_failed` | 标题栏报告写出失败 | 输出目录 | 先修目录和权限，不继续调 SolidWorks API |
| `drawing_title_block_api_evidence_insufficient` | 当前标题栏 API 证据不足 | `title_block_report.json`、官方 API Help、宏录制 | 停止盲改，补充官方证据或最小宏录制后再继续 |

V1.3 只允许写入最小标题栏/图纸属性信息。不得借修复失败扩展到 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4。

## V1.4 工程发布包与质量检查失败

V1.4 发布包失败必须先读取 `package_quality_report.json` 和 `release_manifest.json`，再判断 `failure_stage`。本阶段不启动 SolidWorks，不修复 CAD 几何，不做 PDF 视觉识别。

| failure_stage | 可能原因 | 首先读取 | 处理方式 |
|---|---|---|---|
| `source_artifacts_missing` | V1.0-B 到 V1.3 的 SLDPRT、STEP、SLDDRW 或 PDF 缺失 | `release_manifest.json`、`package_quality_report.json` | 回到对应阶段生成真实输出，不在 V1.4 伪造 CAD 文件 |
| `source_report_missing` | `build_report.json`、`diagnostic_report.json`、`drawing_report.json`、`dimension_report.json` 或 `title_block_report.json` 缺失 | `reports/`、manifest 中的 report 项 | 回到对应阶段补报告或重新运行 smoke test |
| `source_report_failed` | 至少一个源报告 `final_status=Failed`，发布包不能作为可交付物 | `package_quality_report.json` 的 `source_report_failures`、对应源报告 | 回到失败报告所属阶段修复，不把 `package_build_status=Passed` 解释为交付通过 |
| `artifact_copy_failed` | 文件被占用、路径权限不足或复制目标不可写 | Builder 日志、manifest 错误 | 修复文件权限和输出目录后重新生成发布包 |
| `manifest_write_failed` | `release_manifest.json` 写出失败 | 输出目录权限 | 先修复目录和权限，不进入 CAD API 调试 |
| `quality_report_write_failed` | `package_quality_report.json` 写出失败 | 输出目录权限 | 先修复目录和权限，再重新运行 self-check |
| `release_summary_write_failed` | `release_summary.md` 写出失败 | 输出目录权限 | 先修复目录和权限，再重新生成摘要 |
| `package_validation_failed` | 包内路径、文件大小、报告状态字段或失败阶段校验不通过 | `package_quality_report.json` 的 checks | 按失败 check 修复，不扩展到复杂图纸审查 |

V1.4 只做发布包收集与最小质量检查。不得借修复失败扩展到 BOM、装配图、批量出图、国标模板美化、复杂图纸审查、几何 OCR、PDF 视觉识别或 V1.5。

## V1.5 主工作流失败修复

V1.5 主工作流失败必须先读取 `AgentOutput` 中的 `solidworks-main-workflow-report` 元数据、`WorkflowExecutionResult` 步骤和 `QualityGate` 决策。不要让 Agent、Gateway 或 LLM 绕过 `SolidWorksMainWorkflowRunner` 直接调用 Worker。

| failure_stage | 可能原因 | 首先读取 | 处理方式 |
|---|---|---|---|
| `build_plan_generation_failed` | `SolidWorksBuildPlanSkill` 未生成结构化计划 | 主流程步骤日志、Skill 输出 | 修复 Skill 或输入 spec，不进入 Worker |
| `build_plan_validation_failed` | `SolidWorksBuildPlanValidator` 或 Reviewer 拒绝计划 | validation/review issues | 修正计划参数或受控场景，不调用真实 CAD |
| `worker_execution_failed` | Worker 返回 Failed 或 Rejected | Worker result、preflight report、build report | 按 Worker 层 failure_stage 修复，保持双开关 |
| `quality_gate_failed` | ArtifactValidator 或最终 Review 未通过 | QualityGate 决策、artifact validation issues | 修复产物或报告，不绕过 QualityGate |
| `main_workflow_failed` | 未分类主流程失败 | workflow failure report | 保持默认 fake 路径，补充可行动阶段后再继续 |

本地交互式且 `dry_run=false` 时默认真实执行；`SW_DISABLE_REAL_EXECUTION=true`、CI、单元测试或 `SW_FORCE_FAKE_WORKER=true` 时保持 fake / dry-run 主流程可用。

## V1.7 端到端主工作流失败修复

V1.7 首先读取同次 `reports/e2e_execution_report.json`，再读取同目录 `release_manifest.json` 和 `reports/package_quality_report.json`。不得使用历史 latest、SmokeRunner 或 Builder 结果替代本次主流程证据。

| failure_stage | 首先检查 | 修复边界 |
|---|---|---|
| `real_execution_disabled` | dry-run、禁用变量、CI、单元测试、强制 Fake Worker | 确认禁用来源；需要真实执行时从本地交互式 CLI 重跑，不能使用 Fake 回退。 |
| `real_execution_environment_unavailable` | issue 中的操作系统、交互桌面和 `SldWorks.Application` 注册探测结果 | 在受支持的本地交互 Windows 会话安装或修复 SolidWorks COM 注册后重跑；该阶段必须发生在 COM 连接前，不能删除探测或回退 Fake Worker。 |
| `preflight_failed` | `build_report.json`、模板路径、环境 | 修复既有 `SW_TEMPLATE_PART_PATH` 或工程图模板配置，不新增 API。 |
| `source_artifacts_missing` | manifest 的 SourcePath、阶段 report | 只重跑本次失败阶段，保持 request 绑定。 |
| `source_report_missing` / `source_report_failed` | 四个复制后的 report | 回到对应 Build、Drawing、Dimension 或 TitleBlock 阶段。 |
| `real_execution_evidence_failed` | manifest 的 `source_execution_evidence` | 确认 mode、连接、`real_cad_executed` 和阶段 QualityGate，文件存在不足以修复。 |
| `quality_gate_failed` | E2E workflow steps 与总体 gate decision | 修正上游校验/复审失败，不能绕过总体 QualityGate。 |

标题栏阶段仅修复自定义属性的写入、回读、重建、保存和 PDF 导出；不把 Sheet Format 可见渲染失败误当作本阶段已支持的功能。

## V1.7-REAL-AUTH 本地授权后的失败分流

| failure_stage | 首先检查 | 修复边界 |
|---|---|---|
| 本地配置无效 | `config/solidworks.local.json` 的模板、可见性和超时 | 修复可选配置；该文件不再承担授权。 |
| `preflight_failed` | `build_report.json`、`SW_TEMPLATE_PART_PATH`、`SW_TEMPLATE_DRAWING_PATH` | 修复既有模板路径或访问权限，再从 CLI 重跑同一主流程。 |
| `solidworks_connection_failed` | `e2e_execution_report.json` 的启动尝试、连接日志和 COM 注册 | 修复本机 SolidWorks 可连接性；不得改为确认缺失或伪造连接成功。 |
| 其他阶段 failure_stage | 同次阶段报告和 `package_quality_report.json` | 按原有 Build、Drawing、Dimension、TitleBlock 修复路径处理，并保留总体 QualityGate 失败。 |

任何真实执行失败都必须保留实际 `failure_stage` 并进入上述既有修复路径；不得改写为旧式确认缺失。

## V1.8 零件族 Worker 失败分流

### 目标与适用范围

本节处理已进入零件族执行边界的定义、BuildPlan、Builder 和产物失败。若 `CADModelSpec` 本身未注册或参数非法，应回到 `src/Modules/CADModeling/failure_repair.md` 的前置校验路径，不得进入本 Worker。

### 输入与输出

输入为已校验 BuildPlan、Registry 解析的 Builder、Worker 日志、`build_report.json` 和 API evidence。输出必须记录精确 `failure_stage`、零件族、失败 operation、证据路径、修复策略和下一步验证命令。

| `failure_stage` | 首先检查 | 修复策略 | 回填条件 |
|---|---|---|---|
| `part_family_definition_missing` | Registry 键、定义实例、组装根 | 恢复明确注册，不增加 `switch(part_type)` 回退 | Registry 单元测试和三族注册 self-check 通过 |
| `build_plan_generation_failed` | Definition 输出、operation 参数、dependencies | 在对应 Definition 中修复计划映射 | 该族计划快照和 dry-run 测试通过 |
| `part_family_builder_missing` | `PartFamilyBuilderRegistry` 的 Builder 索引与组装注册 | 注册该族 Builder，不由 plate Builder 代执行 | 类型到 Builder 映射测试通过 |
| `flange_build_failed` | 环形基体、中心孔、螺栓孔阵列的 operation 与日志 | dry-run 问题修 `FlangeFeatureBuilder`；真实 API 问题先进独立 flange smoke | dry-run 重现通过；真实回填还要求独立 smoke 及非空 SLDPRT / STEP |
| `shaft_build_failed` | 截面线段、中心线、台阶列表和旋转 operation | dry-run 问题修 `ShaftFeatureBuilder`；真实 API 问题先进旋转专用诊断 Runner | `CreateLine`、`CreateCenterLine`、`FeatureRevolve2` 证据、返回对象和非空产物都验证通过 |
| `artifact_validation_failed` | 产物绝对路径、扩展名、大小、零件族标识与报告状态 | 修复 Builder 或报告语义，不放宽 Validator 伪造通过 | ArtifactValidator、Reviewer 和 QualityGate 全部通过 |

### 执行步骤与验证标准

1. 先确认请求已通过 Registry 与参数 Validator；否则停在 Worker 之前。
2. 用该族 dry-run 复现；`plate_basic_4holes` 还要运行现有回归测试。
3. 涉及真实 API 时，按 API Evidence Driven Repair Loop 进入该族独立 Runner，成功后再回填 Builder。
4. 运行 build、test、默认 self-check，确认修复过程不启动 SolidWorks。

禁止用法兰或轴的 dry-run 报告代替真实诊断报告，禁止无独立 smoke 证据就改动主 Worker，禁止借修复扩展到装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9。

## V1.9 Phase 1 真实 build-only 失败分流

### 目标与输入输出

本节处理已进入 `RealSolidWorksWorker` 的 flange / shaft 真实构建失败。输入是同次 Worker 日志、`PartFamilyBuildResult`、`build_report.json`、诊断证据、产物校验和质量门禁裁决；输出是下列单一失败阶段及可重现修复路径。

| `failure_stage` | Worker 层证据 | 修复与回填条件 |
|---|---|---|
| `part_family_api_evidence_insufficient` | Builder 的 API evidence 状态、独立 diagnostic 路径 | 不连接主 Worker；先在专用 Runner 获得真实 `Passed` 证据。 |
| `flange_profile_create_failed` | 基准面、草图状态、`CreateCircle` 返回值 | 外圆轮廓诊断通过后才允许拉伸。 |
| `flange_extrude_failed` | `FeatureExtrusion2` 参数、Feature 和重建状态 | 圆盘实体存在且无错后回填。 |
| `flange_inner_cut_failed` | 独立活动内孔草图和 `FeatureCut4` | 中心孔真实切通并重建通过后回填。 |
| `flange_bolt_holes_failed` | 孔中心坐标、孔数、单草图和 `FeatureCut4` | 全部螺栓孔存在且与参数一致后回填。 |
| `shaft_profile_create_failed` | `CreateLine`、`CreateCenterLine`、闭合性、台阶映射 | 轮廓闭合且中心线状态可审计后回填。 |
| `shaft_revolve_failed` | selection mark `16`、`FeatureRevolve2` 参数与返回 Feature | 360° 旋转和重建都成功后回填。 |
| `shaft_step_feature_failed` | 台阶数量、每段直径/长度与实际几何 | 参数和几何一致，不使用偏移拉伸回退。 |
| `part_save_failed` | 保存返回值、错误码、目标路径和文件大小 | SLDPRT 真实非空且当次路径一致。 |
| `step_export_failed` | 活动文档、导出返回值、错误码和文件大小 | STEP 真实非空且与当次零件一致。 |
| `artifact_validation_failed` | 两个产物、路径根、扩展名、大小和报告 | 修复 Builder / report，不放宽 ArtifactValidator。 |
| `quality_gate_rejected` | Validator issues、Reviewer issues、GateDecision | 修复上游原因，重跑完整主工作流程。 |

### 执行步骤与验证标准

1. 从当次 `output/solidworks/e2e/<part_type>/<timestamp>/reports/e2e_execution_report.json` 开始，不扫描历史 latest。
2. API 阶段失败先进该族 diagnostic；保存/导出在 Worker 边界修复；校验/门禁在对应层修复。
3. self-check、CI、单元测试和 dry-run 不启动 COM；需要真实重跑时从本地交互式 CLI 按统一策略全局串行执行。

禁止用直接 Builder / SmokeRunner 结果替换主工作流程报告，禁止为修复 flange / shaft 进入工程图，禁止跳过 QualityGate，禁止进入 V2.0。

## V1.9 Phase 2 修复证据

flange 首次 diagnostic 在保存阶段返回 `part_save_failed`。修复复用 plate 已验证的 `SaveAs3` 主调用，并保留 `SaveAs` 回退；后续真实保存和 STEP 导出均生成非空产物。

首次主流程的发布包阶段遇到瞬时源文件哈希读锁并返回 `artifact_copy_failed`。修复后的顺序是先复制、再校验目标，只有两步均成功且源读锁属于已恢复状态时才记录 warning。最终 flange 与 shaft metadata 回填运行均为 `Passed`、`Deliverable`、QualityGate `Passed`。

任何复制失败、目标缺失、空文件或目标校验失败仍是阻断项。禁止把已验证的瞬时读锁分支泛化为发布包错误豁免，也不进入 V2.0。

## V2.0-C Feature Adapter 失败修复

### 目标与证据入口

输入为同次 `feature_execution_report.json`、Handler/Adapter 版本、API evidence、逐步返回结果、重建状态和产物。先确认失败属于纯逻辑 Handler、Adapter、Worker、产物还是证据门禁；不得通过 Handler 直接 COM 临时绕过。

| `failure_stage` | Worker/Adapter 证据 | 修复与回填条件 |
|---|---|---|
| `feature_adapter_missing` | 组合根、接口注册、Worker 注入结果 | 恢复 `ISolidWorksFeatureAdapter` 注入；Handler 仍不创建 Adapter。 |
| `sketch_execution_failed` | 基准选择、草图进入/退出、活动文档和重建 | 草图事务在专用 diagnostic 中完整通过。 |
| `sketch_geometry_create_failed` | line / rectangle / circle 参数、米制转换和返回对象 | 每个预期实体有效并可被后续特征引用。 |
| `extrude_execution_failed` | 闭合草图、blind 参数、Feature 返回和重建 | 有效实体 Feature 与重建结果同时通过。 |
| `cut_execution_failed` | 切割草图、blind 深度、目标选择、`FeatureCut4` 返回 | 切除 Feature、重建和几何结果通过。 |
| `hole_execution_failed` | 圆草图子步骤和 blind `FeatureCut4` 子步骤 | 两个子步骤及孔几何都通过；不改用 Hole Wizard。 |
| `feature_result_invalid` | API 返回、结果标识、依赖、重建和错误集合 | 拒绝“未抛异常”成功，补全结构化结果校验。 |
| `feature_artifact_missing` | 当次 SLDPRT、STEP、报告路径和大小 | 三项同次输出存在、非空且运行标识一致。 |
| `feature_api_unverified` | evidence 状态、Adapter/Handler 版本和参数轮廓 | 先完成专用 diagnostic 与证据审查；`verified` 前不回填生产。 |

### 修复顺序

1. 输入/注册问题先在无 COM 层修复。
2. Adapter 缺失先修组合根和可注入接口。
3. evidence 未验证时停止生产，只允许显式专用 diagnostic。
4. API 问题在 `output/solidworks/features/<timestamp>/` 中隔离复现，不扫描历史 latest。
5. diagnostic 通过后审查证据，再从 `run-cad-workflow` 重跑最终主流程。

禁止在 Handler 内加 COM，禁止用 `SimpleHole2` / Hole Wizard，禁止放宽 Feature 或文件有效性，禁止以 diagnostic 替代 Validator、Reviewer、QualityGate，也不进入 V2.0-D。

### 防假成功证据回填

旧 run `20260730_073759_9143941` 的 Cut/Hole 被人工判定为无孔假成功。非空 COM Feature、`rebuild_passed=true`、非空 SLDPRT/STEP 不能单独关闭 `feature_result_invalid`；该 run 必须从 verified evidence 中撤销。

修复验证使用 `20260730_085830_6592380`，要求 Cut/Hole 各自产生严格体积下降、特征树新增对应 `ICE`，并由等轴测/俯视确认两孔。若任何一项缺失，继续返回 `feature_result_invalid`，不得用 `CandidatePassed` 覆盖。证据 revision、Handler/Adapter 版本、诊断报告或 SolidWorks 版本任一不一致时，返回 `feature_api_unverified`。

新 run 已关闭四个精确 profile 的证据缺口，但仍为 `NotDeliverable`。任何超出 profile 的调用继续使用 `feature_api_unverified`；最终关闭交付问题必须通过 `run-cad-workflow`。

## V2.0-D 重建与几何读取修复

修复从同一 `request_id` 的重建、`GeometryReader`、`GeometryValidator`、`Artifact Validator`、`Reviewer` 和 `QualityGate` 报告开始，绝不使用历史产物、旧诊断或仅文件大小。`failure_stage` 与最小修复边界如下：

| failure_stage | 先检查 | 允许修复 |
|---|---|---|
| rebuild_failed | 当前模型的 Rebuild 错误和 changed_features | 既有 FeatureGraph 参数映射、既有 Handler / Adapter；不新增 Feature。 |
| geometry_read_failed | 当前会话、当前 SLDPRT 和 GeometryReader 的读取异常 | GeometryReader 封装和有证据的读取 API；不把 COM 读进纯逻辑层。 |
| bounding_box_invalid | 单位、Body 选择、近似包围盒与模型期望 | 读取转换或模型参数映射；不把近似 Box 当精确尺寸。 |
| volume_validation_failed | Body 数、逐 Body / 总 Volume 与特征前后变化 | 产生差异的既有 Feature 参数或 Handler 结果。 |
| parameter_geometry_mismatch | 输入 length_mm、diameter_mm 与真实尺寸 | ModelUpdateService 到 SketchDefinition / FeatureDefinition 的映射。 |
| feature_missing_after_rebuild | 特征树中的 Sketch、Extrude、Cut、Hole | 既有 FeatureHandler 的图依赖和执行结果。 |
| geometry_report_failed | 报告字段、可解析性、最终状态和 QualityGate 输入 | 报告契约、DTO 序列化和源报告归集。 |

四孔验证要从真实圆柱几何与特征结果证明四个孔。不能把 V2.0-C 的两孔 evidence、单圆草图、COM 成功或文件存在升级为四孔；若既有受证类型无法表达四孔，返回 feature_api_unverified，而不是调用 pattern、through_all、mid_plane、SimpleHole2 或 Hole Wizard。

修复完成后仅以 `run-cad-workflow --input examples/parameter_update_plate.json` 重新验收。`GeometryValidator`、`Reviewer` 与 `QualityGate` 必须同次通过，才可产生 `SLDPRT`、`STEP`、`geometry_validation_report.json`、`rebuild_report.json`；不得进入 V2.0-E。
## V2.0-D 三圆 profile 证据失效

若重建在 COM 连接前以 `feature_api_unverified` 停止，先读取本次 build report 的 `v2_0_d_three_circle_cut_evidence_verified` 缺失原因，并检查 `V20DThreeCircleCutEvidencePolicy` 的输入 SHA、候选报告 SHA、V2.0-C source revision、三圆实体数、直径、20 mm 边距、盲切深度和操作依赖。不得在 Handler 中临时放宽 circle count，不得直接改 Builder 或跳过 QualityGate。

若证据源、参数映射或候选诊断确实需要改变，应先以同一精确输入重跑专用候选诊断，更新受审查的证据元数据，再重新执行 `run-cad-workflow`；候选成功本身仍不可交付。
