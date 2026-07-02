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

## 修复原则

不能直接瞎改 `FeatureCut` 参数。涉及 API 不确定时，必须生成 `ApiEvidenceReport`，查官方 API、本地参考资料和宏录制结果。只有诊断 Runner 成功后，才能回填 `RealSolidWorksWorker`。

V1.1 工程图失败同样不能只记录 Failed。必须输出 `drawing_report.json`、明确 `failure_stage`，并优先在 `SolidWorksDrawingSmokeRunner` 中隔离验证，再回填 `SolidWorksDrawingBuilder`。
