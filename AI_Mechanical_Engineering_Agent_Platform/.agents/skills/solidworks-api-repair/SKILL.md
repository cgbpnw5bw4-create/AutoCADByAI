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

## V2.0-D 参数重建与几何验证修复

适用范围是 V2.0-D 的真实 `SolidWorks` 重建和 `GeometryReader` 读取故障。开始前读取同一次 `rebuild_report.json`、`geometry_validation_report.json`、`FeatureGraph` 快照、接口证据和 `QualityGate` 结果；不扫描历史产物，不使用旧诊断或文件存在替代当前证据。

1. 先固定唯一 `failure_stage`：`rebuild_failed`、`geometry_read_failed`、`bounding_box_invalid`、`volume_validation_failed`、`parameter_geometry_mismatch`、`feature_missing_after_rebuild` 或 `geometry_report_failed`。
2. 保持边界：`ModelUpdateService` 和 `GeometryValidator` 是纯逻辑层；只有 `ISolidWorksGeometryReader` / `RealSolidWorksGeometryReader` 可以读取 `COM`，并且只读当前受控模型。
3. 若新增 `IPartDoc.GetBodies2`、`GetPartBox`、`IBody2.GetMassProperties`、质量属性、特征遍历、`GetExtremePoint` 或圆柱面读取，先补官方证据、最小诊断、运行时版本和源码修订；证据不足返回 `feature_api_unverified`。
4. 修复参数传播时只修 `CADModelSpec` 到既有 `SketchDefinition` / `FeatureDefinition` / `FeatureGraph` 的映射；不得在 Worker 或 Handler 中直接写 `COM` 业务逻辑。
5. 四孔 `plate_basic_4holes` 必须用真实几何逐孔证明。只能复用已注册、已取证 Feature 类型；禁止新增 CAD Feature、零件族、重复 Agent、阵列、贯穿、对称拉伸或孔向导。
6. 只有 `run-cad-workflow --input examples/parameter_update_plate.json` 可以关闭真实验收。它必须覆盖 160×80×12 初始建模和 200×100×15 更新重建，并让 `GeometryValidator`、`Reviewer`、`QualityGate` 同次通过。

任何 failure_stage、报告字段、真实几何或 QualityGate 未关闭时都不得交付 SLDPRT/STEP，也不得进入 V2.0-E。

## V2.1-B 孔证据修复补充

### 目标、范围与输入输出

处理四类孔的 API、引用和几何证据缺口。输入为 `HoleFeatureDefinition`、当次诊断/几何报告与当前源码；输出为逐类型证据和受控失败结论。先读 `src/Workers/SolidWorks/Features/Hole/api_evidence.md`、`failure_repair.md` 及 `docs/v2_1_b_hole_features.md`。

### 执行与验证

先区分普通孔、沉孔、沉头孔与攻丝孔，再验证 API 名称、官方来源、参数映射、单位、前置选择、返回、状态、失败模式和宏证据。保持唯一 Handler 和统一 Adapter；新显式四类未取证时在 COM 前拒绝，攻丝使用 `tapped_hole_api_unverified`。修改绑定源码须重新采证，不能直接改旧报告修订。真实几何须逐孔证明数量、直径、深度、沉孔台阶、沉头锥面和攻丝孔向导元数据；诊断不能代替最终 QualityGate。

### 失败与禁止

普通圆加 Cut、装饰螺纹、孔向导攻丝、真实建模螺纹分开取证。不得将 `thread_pitch` 放入 `HoleWizard5.Length`，不得猜标准数据库枚举、复制脚本、Handler COM、伪造读回或进入 V2.1-C。修复后执行完整 build、test、self-check，保留总体失败与本阶段结果的区别。
