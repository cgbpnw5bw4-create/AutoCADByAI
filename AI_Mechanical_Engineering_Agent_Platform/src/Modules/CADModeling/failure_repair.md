# CADModeling 失败修复说明

## 统一入口

先读取最新报告：

- `output/reports/platform_self_check_report.json`
- 最新 `output/solidworks/diagnostics/**/diagnostic_report.json`
- 最新 `output/solidworks/diagnostics/api_evidence/*.json`

## 已知 failure_stage

| failure_stage | 可能原因 | 首先读取 | 检查字段 | 诊断 Runner | API Evidence | 宏录制 | 回填条件 |
|---|---|---|---|---|---|---|---|
| `connection_failed` | COM 未注册、SolidWorks 未安装或未启动 | diagnostic report | `solidworks_connected`、`errors` | `SolidWorksSmokeRunner` | 否 | 否 | 连接 smoke test 成功 |
| `new_part_failed` | 模板缺失或 Part 创建失败 | diagnostic report | `active_doc_title` | `SolidWorksSmokeRunner` | 视情况 | 可选 | 新建 Part 成功 |
| `plane_selection_failed` | 中英文基准面名称不同 | diagnostic report | `available_reference_planes` | `SolidWorksSmokeRunner` | 否 | 可选 | `SolidWorksPlaneSelector` 成功 |
| `sketch_failed` | 草图未进入、坐标或 API 错误 | diagnostic report | `operations`、`errors` | `SolidWorksSmokeRunner` | 是 | 可选 | 草图最小流程成功 |
| `extrude_failed` | 拉伸参数或选择状态错误 | diagnostic report | `extrude_started` | `SolidWorksSmokeRunner` | 是 | 可选 | 拉伸成功 |
| `cut_holes_failed` | 孔草图选择状态或 `FeatureCut` 参数错误 | diagnostic report 和 evidence | `api_evidence_report_path`、`repair_failure_reason` | `SolidWorksSmokeRunner` | 必须 | 建议 | Runner 成功切孔后回填 |
| `save_sldprt_failed` | 保存路径、权限或 `SaveAs` 参数错误 | build report | `sldprt_save_*` | `SolidWorksSmokeRunner` | 是 | 可选 | SLDPRT 存在且 size > 0 |
| `export_step_failed` | 活动文档、选择状态或 STEP 导出参数错误 | build report | `step_export_*`、`active_doc_title_*` | `SolidWorksSmokeRunner` | 是 | 可选 | STEP 存在且 size > 0 |
| `build_report_write_failed` | 输出目录或权限问题 | build report | `issues`、`output_directory` | 不需要真实 CAD | 否 | 否 | 报告可写 |
| `diagnostic_report_missing` | Runner 未启动或路径错误 | self-check report | `solidworks_latest_diagnostic_report_path` | 手动运行 Runner | 否 | 否 | 报告生成 |
| `source_part_missing` | 工程图源零件缺失或路径错误 | drawing report | `source_part_path` | `SolidWorksDrawingSmokeRunner` | 否 | 否 | 源 SLDPRT 存在且可打开 |
| `drawing_template_missing` | 工程图模板缺失 | drawing report | `drawing_template`、环境变量 | `SolidWorksDrawingSmokeRunner` | 视情况 | 可选 | `.drwdot` 模板可用 |
| `drawing_document_create_failed` | Drawing 文档创建失败 | drawing report | `operations`、`errors` | `SolidWorksDrawingSmokeRunner` | 是 | 可选 | Drawing 新建成功 |
| `source_part_open_failed` | 源零件打开失败 | drawing report | `source_part_path` | `SolidWorksDrawingSmokeRunner` | 否 | 否 | 零件可打开 |
| `source_part_activate_failed` | 源零件激活失败 | drawing report | `operations` | `SolidWorksDrawingSmokeRunner` | 是 | 可选 | `ActivateDoc3` 成功 |
| `front_view_create_failed` | Front 视图创建失败 | drawing report | `views_created` | `SolidWorksDrawingSmokeRunner` | 是 | 可选 | Front 视图创建成功 |
| `top_view_create_failed` | Top 视图创建失败 | drawing report | `views_created` | `SolidWorksDrawingSmokeRunner` | 是 | 可选 | Top 视图创建成功 |
| `right_view_create_failed` | Right 视图创建失败 | drawing report | `views_created` | `SolidWorksDrawingSmokeRunner` | 是 | 可选 | Right 视图创建成功 |
| `isometric_view_create_failed` | Isometric 视图创建失败 | drawing report | `views_created` | `SolidWorksDrawingSmokeRunner` | 是 | 可选 | Isometric 视图创建成功 |
| `slddrw_save_failed` | 工程图保存失败 | drawing report | `slddrw_path`、`slddrw_size_bytes` | `SolidWorksDrawingSmokeRunner` | 是 | 可选 | SLDDRW 存在且 size > 0 |
| `pdf_export_failed` | PDF 导出失败 | drawing report | `pdf_path`、`pdf_size_bytes` | `SolidWorksDrawingSmokeRunner` | 是 | 可选 | PDF 存在且 size > 0 |
| `drawing_report_write_failed` | 工程图报告写出失败 | 输出目录 | `output_directory` | 不需要真实 CAD | 否 | 否 | 报告可写 |
| `source_drawing_missing` | 尺寸标注源工程图缺失 | dimension report | `source_drawing_path` | `SolidWorksDrawingDimensionSmokeRunner` | 否 | 否 | V1.1 SLDDRW 存在 |
| `source_drawing_open_failed` | 源工程图打开失败 | dimension report | `source_drawing_path`、`errors` | `SolidWorksDrawingDimensionSmokeRunner` | 视情况 | 可选 | 工程图可打开 |
| `drawing_view_missing` | 基础视图缺失 | dimension report | `views_confirmed` | `SolidWorksDrawingDimensionSmokeRunner` | 否 | 否 | 回到 V1.1 修复四视图 |
| `drawing_view_activate_failed` | 视图激活失败 | dimension report | `views_confirmed`、`operations` | `SolidWorksDrawingDimensionSmokeRunner` | 是 | 可选 | 视图可激活 |
| `length_dimension_failed` | 160 mm 长度尺寸失败 | dimension report | `dimensions` | `SolidWorksDrawingDimensionSmokeRunner` | 是 | 可选 | 长度尺寸成功 |
| `width_dimension_failed` | 80 mm 宽度尺寸失败 | dimension report | `dimensions` | `SolidWorksDrawingDimensionSmokeRunner` | 是 | 可选 | 宽度尺寸成功 |
| `thickness_dimension_failed` | 12 mm 厚度尺寸失败 | dimension report | `dimensions` | `SolidWorksDrawingDimensionSmokeRunner` | 是 | 可选 | 厚度尺寸成功 |
| `hole_diameter_dimension_failed` | Φ10 孔径尺寸失败 | dimension report | `dimensions` | `SolidWorksDrawingDimensionSmokeRunner` | 是 | 可选 | 孔径尺寸成功 |
| `hole_position_dimension_failed` | 孔中心距尺寸失败 | dimension report | `dimensions` | `SolidWorksDrawingDimensionSmokeRunner` | 是 | 可选 | 120 mm 与 40 mm 中心距成功 |
| `dimension_save_failed` | 带尺寸工程图保存失败 | dimension report | `slddrw_path`、`slddrw_size_bytes` | `SolidWorksDrawingDimensionSmokeRunner` | 是 | 可选 | SLDDRW 存在且 size > 0 |
| `dimension_pdf_export_failed` | 带尺寸 PDF 导出失败 | dimension report | `pdf_path`、`pdf_size_bytes` | `SolidWorksDrawingDimensionSmokeRunner` | 是 | 可选 | PDF 存在且 size > 0 |
| `dimension_report_write_failed` | 尺寸报告写出失败 | 输出目录 | `output_directory` | 不需要真实 CAD | 否 | 否 | 报告可写 |
| `drawing_dimension_api_evidence_insufficient` | 尺寸 API 证据不足 | dimension report 和官方 API | `dimensions`、`errors` | `SolidWorksDrawingDimensionSmokeRunner` | 必须 | 建议 | 证据充分后再回填 |
| `source_dimensioned_drawing_missing` | 标题栏源带尺寸工程图缺失 | title block report | `source_dimensioned_drawing_path` | `SolidWorksDrawingTitleBlockSmokeRunner` | 否 | 否 | V1.2 SLDDRW 存在 |
| `title_block_template_missing` | Sheet 或标题栏模板不可读 | title block report | `title_block_template_detected` | `SolidWorksDrawingTitleBlockSmokeRunner` | 视情况 | 可选 | 当前 Sheet 可读 |
| `custom_property_write_failed` | 自定义属性写入失败 | title block report | `properties` | `SolidWorksDrawingTitleBlockSmokeRunner` | 是 | 可选 | `PartName` 等属性写入成功 |
| `drawing_property_read_failed` | 图纸属性或比例读取失败 | title block report | `drawing_properties_read`、`scale` | `SolidWorksDrawingTitleBlockSmokeRunner` | 是 | 可选 | 比例读取或记录为 `auto` |
| `title_block_update_failed` | 标题栏字段刷新失败 | title block report | `title_block_updated`、`operations` | `SolidWorksDrawingTitleBlockSmokeRunner` | 是 | 可选 | 工程图刷新成功 |
| `title_block_save_failed` | 带标题栏工程图保存失败 | title block report | `slddrw_path`、`slddrw_size_bytes` | `SolidWorksDrawingTitleBlockSmokeRunner` | 是 | 可选 | SLDDRW 存在且 size > 0 |
| `title_block_pdf_export_failed` | 带标题栏 PDF 导出失败 | title block report | `pdf_path`、`pdf_size_bytes` | `SolidWorksDrawingTitleBlockSmokeRunner` | 是 | 可选 | PDF 存在且 size > 0 |
| `title_block_report_write_failed` | 标题栏报告写出失败 | 输出目录 | `output_directory` | 不需要真实 CAD | 否 | 否 | 报告可写 |
| `drawing_title_block_api_evidence_insufficient` | 标题栏 API 证据不足 | title block report 和官方 API | `properties`、`errors` | `SolidWorksDrawingTitleBlockSmokeRunner` | 必须 | 建议 | 证据充分后再回填 |
| `source_artifacts_missing` | 发布包源 SLDPRT、STEP、SLDDRW 或 PDF 缺失 | `release_manifest.json`、`package_quality_report.json` | artifacts 列表 | 不需要真实 CAD | 否 | 否 | 回到对应阶段生成真实输出 |
| `source_report_missing` | 发布包源报告缺失 | `release_manifest.json`、`package_quality_report.json` | reports 列表 | 不需要真实 CAD | 否 | 否 | 回到对应阶段补报告 |
| `source_report_failed` | 发布包源报告已收集但至少一个 `final_status=Failed` | `package_quality_report.json` | `source_report_failures`、`deliverable_status` | 不需要真实 CAD | 否 | 否 | 回到失败源报告所属阶段修复 |
| `artifact_copy_failed` | 发布包复制文件失败 | Builder 日志 | `errors` | 不需要真实 CAD | 否 | 否 | 修复权限或文件占用 |
| `manifest_write_failed` | `release_manifest.json` 写出失败 | 输出目录 | `output_directory` | 不需要真实 CAD | 否 | 否 | manifest 可写 |
| `quality_report_write_failed` | `package_quality_report.json` 写出失败 | 输出目录 | `output_directory` | 不需要真实 CAD | 否 | 否 | quality report 可写 |
| `release_summary_write_failed` | `release_summary.md` 写出失败 | 输出目录 | `output_directory` | 不需要真实 CAD | 否 | 否 | summary 可写 |
| `package_validation_failed` | 发布包路径、大小、PDF 或报告状态校验失败 | `package_quality_report.json` | `checks` | 不需要真实 CAD | 否 | 否 | 所有最小检查通过 |

## 修复原则

不能直接瞎改 `FeatureCut` 参数。涉及 API 不确定时，必须生成 `ApiEvidenceReport`，查官方 API、本地参考资料和宏录制结果。只有诊断 Runner 成功后，才能回填 `RealSolidWorksWorker`。

V1.1 工程图失败同样不能只记录 Failed。必须输出 `drawing_report.json`、明确 `failure_stage`，并优先在 `SolidWorksDrawingSmokeRunner` 中隔离验证，再回填 `SolidWorksDrawingBuilder`。

V1.2 尺寸标注失败必须输出 `dimension_report.json`、明确 `failure_stage`，并优先在 `SolidWorksDrawingDimensionSmokeRunner` 中隔离验证，再回填 `SolidWorksDrawingDimensionBuilder`。不得借 V1.2 修复进入 BOM、标题栏、自动全尺寸标注、复杂公差或 V1.3。

V1.3 标题栏基础信息失败必须输出 `title_block_report.json`、明确 `failure_stage`，并优先在 `SolidWorksDrawingTitleBlockSmokeRunner` 中隔离验证，再回填 `SolidWorksDrawingTitleBlockBuilder`。不得借 V1.3 修复进入 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4。

V1.4 发布包失败必须输出 `release_manifest.json`、`package_quality_report.json` 和 `release_summary.md`，并明确 `failure_stage`。本阶段只做文件收集和最小质量检查，不得借 V1.4 修复进入 BOM、装配图、批量出图、国标模板美化、复杂图纸审查、几何 OCR、PDF 视觉识别或 V1.5。

V1.5 主流程失败必须先看 `SolidWorksMainWorkflowRunner` 的工作流步骤、Worker result、ArtifactValidator 和 QualityGate 决策。若 `package_build_status=Passed` 但 `deliverable_status=NotDeliverable`，说明打包过程成功但源报告未全部通过，必须回到失败源报告所属阶段修复。
