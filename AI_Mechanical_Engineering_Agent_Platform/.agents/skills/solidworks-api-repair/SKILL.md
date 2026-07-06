---
name: solidworks-api-repair
description: "用于 `SolidWorks` `API` 调用失败的证据驱动修复流程。适用于 `plane_selection_failed`、`cut_holes_failed`、`save_sldprt_failed`、`export_step_failed` 等诊断阶段。"
---

# SolidWorks API 修复技能

## 适用范围

当 `SolidWorks` API、COM 或导出流程失败时使用本技能。常见阶段包括 `plane_selection_failed`、`cut_holes_failed`、`save_sldprt_failed`、`export_step_failed` 和 V1.1 工程图阶段的 `source_part_missing`、`drawing_template_missing`、`drawing_document_create_failed`、`front_view_create_failed`、`top_view_create_failed`、`right_view_create_failed`、`isometric_view_create_failed`、`slddrw_save_failed`、`pdf_export_failed`。V1.2 尺寸阶段还包括 `source_drawing_missing`、`source_drawing_open_failed`、`drawing_view_missing`、`drawing_view_activate_failed`、`length_dimension_failed`、`width_dimension_failed`、`thickness_dimension_failed`、`hole_diameter_dimension_failed`、`hole_position_dimension_failed`、`dimension_save_failed`、`dimension_pdf_export_failed`、`dimension_report_write_failed`、`drawing_dimension_api_evidence_insufficient`。V1.3 标题栏阶段还包括 `source_dimensioned_drawing_missing`、`title_block_template_missing`、`custom_property_write_failed`、`drawing_property_read_failed`、`title_block_update_failed`、`title_block_save_failed`、`title_block_pdf_export_failed`、`title_block_report_write_failed`、`drawing_title_block_api_evidence_insufficient`。

## 必须读取

- `src/Workers/SolidWorks/failure_repair.md`
- `src/Workers/SolidWorks/api_evidence.md`
- 最新 `diagnostic_report.json`
- 如涉及工程图，读取最新 `drawing_report.json`
- 如涉及工程图尺寸，读取最新 `dimension_report.json`
- 如涉及工程图标题栏，读取最新 `title_block_report.json`
- 如存在，读取 `output/solidworks/diagnostics/api_evidence/*.json`

## 执行步骤

1. 读取诊断报告并确认 `failure_stage`。
2. 查官方 API、本地参考资料和已有 evidence。
3. 生成或更新 `ApiEvidenceReport`。
4. 只做最小封装修复。
5. 先在 `SolidWorksSmokeRunner` 中验证。
6. 验证成功后再回填 `RealSolidWorksWorker`。

工程图尺寸失败时，优先在 `SolidWorksDrawingDimensionSmokeRunner` 中复现。`CreateLinearDim4` 与 `ICreateDiamDim4` 证据不足时，输出 `drawing_dimension_api_evidence_insufficient`，不要扩展到自动全尺寸标注或宏生产路径。

工程图标题栏失败时，优先在 `SolidWorksDrawingTitleBlockSmokeRunner` 中复现。`CustomPropertyManager`、`Add3`、`Set2`、`Get6`、`GetCurrentSheet` 或 `GetProperties2` 证据不足时，输出 `drawing_title_block_api_evidence_insufficient`，不要扩展到 BOM、复杂国标模板、明细栏或宏生产路径。

## 禁止事项

- 不复制第三方 `scripts`。
- 不盲改 COM API 长参数。
- 不自动执行宏。
- 不默认启动 SolidWorks。
- 不让 Agent、Gateway 或 LLM 直接调用 Worker。
- 不新增同职责 Codex Agent；新 API 证据写入本 Skill 或 `api_evidence.md`。
