---
name: solidworks-api-repair
description: "用于 `SolidWorks` `API` 调用失败的证据驱动修复流程。适用于 `plane_selection_failed`、`cut_holes_failed`、`save_sldprt_failed`、`export_step_failed` 等诊断阶段。"
---

# SolidWorks API 修复技能

## 适用范围

当 `SolidWorks` API、COM 或导出流程失败时使用本技能。常见阶段包括 `plane_selection_failed`、`cut_holes_failed`、`save_sldprt_failed`、`export_step_failed` 和 V1.1 工程图阶段的 `source_part_missing`、`drawing_template_missing`、`drawing_document_create_failed`、`front_view_create_failed`、`top_view_create_failed`、`right_view_create_failed`、`isometric_view_create_failed`、`slddrw_save_failed`、`pdf_export_failed`。

## 必须读取

- `src/Workers/SolidWorks/failure_repair.md`
- `src/Workers/SolidWorks/api_evidence.md`
- 最新 `diagnostic_report.json`
- 如涉及工程图，读取最新 `drawing_report.json`
- 如存在，读取 `output/solidworks/diagnostics/api_evidence/*.json`

## 执行步骤

1. 读取诊断报告并确认 `failure_stage`。
2. 查官方 API、本地参考资料和已有 evidence。
3. 生成或更新 `ApiEvidenceReport`。
4. 只做最小封装修复。
5. 先在 `SolidWorksSmokeRunner` 中验证。
6. 验证成功后再回填 `RealSolidWorksWorker`。

## 禁止事项

- 不复制第三方 `scripts`。
- 不盲改 COM API 长参数。
- 不自动执行宏。
- 不默认启动 SolidWorks。
- 不让 Agent、Gateway 或 LLM 直接调用 Worker。
- 不新增同职责 Codex Agent；新 API 证据写入本 Skill 或 `api_evidence.md`。
