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

## V1.7 主流程端到端失败修复

- `real_execution_disabled`：检查 `dry_run`、`SW_DISABLE_REAL_EXECUTION`、CI、单元测试和 `SW_FORCE_FAKE_WORKER`。不得把 Fake Worker 回退结果当作真实成功。
- `preflight_failed`、`drawing_template_missing` 或 `title_block_template_missing`：保留 E2E report，检查已有模板环境变量和原阶段报告；不要为本轮新增 CAD API 或绕过模板预检。
- `source_artifacts_missing`、`source_report_missing`、`source_report_failed` 或 `real_execution_evidence_failed`：读取同次 `release_manifest.json`、`package_quality_report.json` 与 `e2e_execution_report.json`。只修复该 request 的失败阶段，不读取历史 latest 或 SmokeRunner 作为最终通过证据。
- `quality_gate_failed`：确认四阶段均已经过 ArtifactValidator、Reviewer 和 QualityGate；即使某一阶段失败，也必须保留总体 QualityGate 的拒绝/失败结论。

## V1.8 零件族前置失败修复

### 目标与适用范围

本节处理结构化输入到 `PartFamilyBuilder` 之间的失败。未注册类型和非法参数属于 Worker 之前的可预期拒绝，修复时不得尝试连接 SolidWorks。

### 输入与输出

输入为原始 `CADModelSpec`、Registry 查找结果、零件族校验结果和 BuildPlan 生成结果。输出必须包含稳定 `failure_stage`、直接原因、证据来源、修复策略和下一步验证命令。

| `failure_stage` | 直接原因 | 首先读取 | 修复与回填条件 |
|---|---|---|---|
| `unsupported_part_type` | `part_type` 未注册 | `CADModelSpec`、Registry 已注册键 | 更正输入，或在独立定义、测试和文档齐全后显式注册；不得默认回退为 `plate_basic_4holes`。 |
| `missing_required_parameter` | 该族 Schema 中的必需字段缺失 | 零件族 Schema、validation issues | 补齐缺失字段；验证 Worker 调用计数仍为零。 |
| `invalid_parameter_value` | 数值不是有限正数，或零件族内参数关系非法 | 具体参数名、原值、校验规则 | 修正输入并重跑纯校验测试；不得在 Worker 内强制截断或替换为默认值。 |
| `part_family_definition_missing` | Registry 条目缺少 `IPartFamilyDefinition` | Registry 注册日志、组装根 | 恢复完整定义并补注册测试。 |
| `build_plan_generation_failed` | 零件族定义无法把合法参数转为 BuildPlan | 定义输出、计划 operations 和 dependencies | 在该族定义内做最小修复，不向通用 Skill 增加类型分支。 |
| `part_family_builder_missing` | BuildPlan 有定义但无对应 Builder | Registry 条目、Builder 注册表 | 补齐该族 Builder 与 dry-run 测试，不得使用通用 `switch(part_type)` 代执行。 |
| `flange_build_failed` | `flange_basic` Builder 生成失败 | flange BuildPlan、Builder 日志、API evidence | dry-run 失败先修计划或 Builder；真实失败必须先进独立 smoke，再回填主 Worker。 |
| `shaft_build_failed` | `shaft_basic` Builder 生成失败 | shaft BuildPlan、Builder 日志、旋转 API evidence | dry-run 失败先修复台阶数组映射；真实路径必须有专用诊断证据。 |
| `artifact_validation_failed` | Worker 返回的文件、扩展名、大小或证据不合格 | ArtifactValidator issues、Worker report | 修复产物或报告，不得跳过 Validator 进入 Drawing 或 QualityGate。 |

### 执行步骤与验证标准

1. 确认 `failure_stage` 来自通用 Validator、Registry、零件族 Validator、BuildPlan 或 Builder 中的单一责任层。
2. 用对应零件族的最小单元测试复现，并确认非法输入未调用 Worker。
3. 修复后运行 build、test、self-check；对 `flange_basic` 和 `shaft_basic` 至少要求 dry-run 通过。

禁止为修复单个失败而增加大型 `switch(part_type)`、绕过 Registry、启动默认真实 CAD，或扩展到装配体、BOM、键槽、螺纹、法兰密封面、批量队列与 V1.9。

## V1.9 Phase 1 build-only 失败修复

### 目标与输入输出

本节将 `flange_basic` 和 `shaft_basic` 真实构建失败定位到单一可行动阶段。输入为同次 `build_report.json`、`e2e_execution_report.json`、Registry / Builder 日志、API evidence、ArtifactValidator 和 QualityGate 结果；输出必须包含直接原因、证据路径、修复策略和下一步验证命令。

| `failure_stage` | 首先检查 | 修复策略 |
|---|---|---|
| `part_family_api_evidence_insufficient` | 官方 API、独立 diagnostic report、完整参数与返回值 | 停止主 Worker 回填，先在对应零件族专用 Runner 补证据。 |
| `flange_profile_create_failed` | 外圆草图、基准面、单位和 `CreateCircle` 返回值 | 在 flange diagnostic 中修复外圆轮廓，不进入拉伸。 |
| `flange_extrude_failed` | 外圆轮廓状态、`FeatureExtrusion2` 参数和 Feature 返回值 | 复用 plate 已验证参数语义，独立验证圆盘拉伸。 |
| `flange_inner_cut_failed` | 独立活动内孔草图、`FeatureCut4` 返回值 | 保持中心孔与螺栓孔分步，不合并失败语义。 |
| `flange_bolt_holes_failed` | 分布圆坐标、孔数、单草图状态和 `FeatureCut4` | 修正孔中心计算或草图，不改用 `HoleWizard` / 圆周阵列。 |
| `shaft_profile_create_failed` | `CreateLine` 闭合轮廓、`CreateCenterLine`、台阶序列 | 先在 shaft diagnostic 验证闭合性和旋转轴线，不进入旋转。 |
| `shaft_revolve_failed` | selection mark `16`、`FeatureRevolve2` 完整参数和返回 Feature | 对照官方 360° 旋转证据，不切换偏移拉伸。 |
| `shaft_step_feature_failed` | 台阶参数对、轮廓线段顺序、重建后几何 | 修正台阶到闭合轮廓的映射，仍使用同一旋转策略。 |
| `part_save_failed` | SLDPRT 保存返回值、路径、文件锁和大小 | 修复保存边界，非空文件前不得进入 STEP。 |
| `step_export_failed` | 活动文档、STEP 导出返回值、路径和大小 | 激活当次零件后重跑导出，不复用历史 STEP。 |
| `artifact_validation_failed` | SLDPRT / STEP 存在、大小、绝对路径、零件族和报告一致性 | 修复产物或报告，不放宽 Validator。 |
| `quality_gate_rejected` | Validator、Reviewer 问题和 GateDecision | 修复上游证据或工程合理性，不绕过 QualityGate。 |

### 执行和验证标准

1. 只读取同次 `output/solidworks/e2e/<part_type>/<timestamp>/` 中的显式报告和 manifest。
2. API 失败先进独立 diagnostic，其他失败在 Worker、Validator、Reviewer 或 QualityGate 对应层修复。
3. 修复后先运行 build / test / self-check，再按 V2.0 本地交互默认策略串行重跑真实主工作流程。

禁止读取历史 latest 修补当次包，禁止用 Builder / SmokeRunner 结果冒充最终验收，禁止 flange / shaft 自动工程图，禁止进入 V2.0。

## V1.9 Phase 2 已验证修复

- flange 首次 diagnostic 的 `part_save_failed` 已通过 plate 验证过的 `SaveAs3` 主路径与 `SaveAs` 回退路径修复；后续 diagnostic 产物非空并为 `CandidatePassed`。
- 首次主流程遇到瞬时源文件哈希读锁并返回 `artifact_copy_failed`。当前仅在复制成功且目标文件校验通过时，将已经恢复的源读锁降级为 warning；最终 metadata 回填主流程已重跑通过。
- 若目标复制、大小或校验失败，仍必须保留 `artifact_copy_failed` 并停止发布，不能套用上述 warning 分支。

这些修复只关闭 V1.9 基础零件族的已知保存与瞬时读锁问题，不扩大到工程图或 V2.0。

## V2.0-A Schema、FeatureGraph 与编译失败

### 目标与适用范围

本节处理 canonical `CADModelSpec` 到 `SolidWorksBuildPlan` 的纯校验和编译失败。输入为原始 Schema、草图、特征图和编译 issues；输出为单一 `failure_stage`、直接原因、修复后的 Schema 与 dry-run 结果。任何失败都不得进入 Worker 或连接 COM。

| `failure_stage` | 直接原因 | 修复与回填条件 |
|---|---|---|
| `invalid_cad_model_spec` | `model_id`、`model_type`、`unit`、草图标识或特征数组不合法 | 修正 canonical 字段；有效 Schema 后重新编译。 |
| `feature_id_missing` | 特征没有稳定标识 | 补非空 `feature_id`，同步修正引用。 |
| `duplicate_feature_id` | 多个特征标识重复 | 为节点分配唯一标识并重建依赖。 |
| `sketch_reference_missing` | 草图缺基准引用，或特征引用未知草图 | 修正 `reference_plane` / `referenced_sketches`。 |
| `feature_dependency_missing` | 特征依赖不存在 | 补齐上游节点或修正依赖标识。 |
| `feature_dependency_cycle` | 有向特征图存在环 | 重新设计依赖；不得强制使用输入顺序。 |
| `unsupported_sketch_entity` | 实体未命名或类型不受支持 | 使用八类受支持实体，或延期扩展。 |
| `unsupported_constraint` | 约束类型不支持或引用未知实体 | 修正约束和同草图实体引用。 |
| `unsupported_feature_type` | 特征不在十类映射中 | 使用受支持类型，或延期到后续阶段。 |
| `invalid_feature_parameter` | 参数、单位或目标引用不符合特征语义 | 修正输入；不得由 Handler 猜测。 |
| `invalid_feature_order` | 顺序非正、重复或早于依赖 | 修正顺序，或删除非必需显式顺序。 |
| `build_plan_compile_failed` | 已校验结构仍无法产生计划 | 保留编译 issues，修复编译器边界并重跑 dry-run。 |

### 修复步骤

1. 先验证 canonical 字段和零件族参数，不读取历史产物。
2. 独立运行草图引用与实体/约束校验。
3. 用 `FeatureGraph.ValidateAndSort()` 复现缺失依赖、环或顺序问题。
4. 只在图有效后调用 `BuildPlanCompiler`。
5. 通过 BuildPlan Validator、Reviewer 和 dry-run 后关闭问题。

不得为修复单图而回到手写 operation，不得跳过图校验，不得调用 V1.9 专用真实 Builder 证明任意图可执行。禁止装配体、队列、通用真实 COM Handler 和 V2.0-B。

## V2.0-B Feature Handler 失败修复

### 目标与输入输出

本节处理 BuildPlan operation 适配、Handler 注册、参数校验和 API evidence 门禁失败。输入为服务端执行来源、计划操作、Registry 快照、Handler validation issues 与 `FeatureApiEvidence`；输出为稳定 `failure_stage`、直接原因、修复证据和无 COM 回归结果。

| `failure_stage` | 直接原因 | 修复步骤 | 关闭条件 |
|---|---|---|---|
| `unsupported_feature_type` | operation 没有 Handler 适配，或 Registry 未注册对应类型 | 确认类型拼写；需要新能力时补独立 Handler、Schema、测试、证据和注册 | 默认或扩展 Registry 可解析，未知类型仍受控拒绝，Worker 无大型 switch |
| `invalid_feature_parameter` | 必需参数缺失、JSON 类型错误、数值或枚举超界 | 读取对应 Handler `ParameterSchema` 和 validation issues，修正输入后重跑纯校验 | 参数合法，非法样例仍在 COM 前拒绝 |
| `sketch_reference_missing` | `reference_plane` 或草图目标引用为空 | 修正 canonical reference 和 operation target | 引用可解析，草图校验通过 |
| `unsupported_sketch_entity` | 草图实体不在 Handler 声明集合或缺稳定类型 | 使用已支持实体，或新增完整实现、测试与证据 | 未支持实体仍拒绝，支持实体通过无 COM 校验 |
| `feature_api_evidence_insufficient` | 整图至少一个 Handler 的证据状态不是 `verified` | 停止 Worker 回填；按对应 `api_evidence.md` 建独立诊断，记录版本、参数、返回值、重建和产物 | 证据经审查为 `verified`，且同参数轮廓的失败与成功测试齐全 |

### 整图预检修复顺序

1. 确认计划是否由服务端标为 `feature_handler_graph`；不得从请求 JSON 补写来源。
2. 适配整张图全部几何 operation，先关闭 `unsupported_feature_type`。
3. 对全部 Handler 运行参数校验，先关闭所有输入问题。
4. 汇总全部 Handler evidence；任一 `unverified` 都以 `feature_api_evidence_insufficient` 停止。
5. 只在整图全部证据为 `verified` 后才允许连接 COM，再进入 ArtifactValidator、Reviewer 与 QualityGate。

五类 Handler 当前全部为 `unverified`，所以正常结果是 COM 连接计数为零。V1.9 的 plate、flange、shaft 专用证据不能用于修改通用 Handler 状态。`ExecuteAsync` 的二次门禁也必须保留，不能只依赖 Worker 预检。

### 禁止事项

禁止为单个失败增加大型 `switch(feature_type)`，禁止部分执行图，禁止把 Fake Worker 或历史零件族产物当作通用真实成功，禁止未经独立诊断将状态改为 `verified`，禁止复制第三方代码，也不得借修复扩展装配体、BOM、队列或后续阶段。

## V2.0-C Feature Adapter 失败修复

### 输入与输出

输入为同次 FeatureGraph、Handler validation、Adapter 解析结果、逐特征执行结果、`feature_execution_report.json` 和产物校验。输出必须是以下单一 `failure_stage`、直接原因、证据路径、修复动作和重跑入口。

| `failure_stage` | 直接原因 | 修复动作 | 关闭条件 |
|---|---|---|---|
| `feature_adapter_missing` | Worker 无真实 Adapter 或注入失败 | 修复组合根和接口注册；不在 Handler 内创建实现 | Worker 解析同一 Adapter，测试仍可注入 fake |
| `sketch_execution_failed` | 基准选择、进入/退出草图或事务失败 | 先修引用和草图状态，再运行专用 diagnostic | 草图事务和重建均通过 |
| `sketch_geometry_create_failed` | line、rectangle 或 circle 返回无效 | 核对坐标、米制转换和返回对象 | 每个实体有效且可被后续特征消费 |
| `extrude_execution_failed` | blind extrude 调用、返回或重建失败 | 核对闭合草图、深度与长参数 | 返回有效 Feature，重建和实体结果通过 |
| `cut_execution_failed` | blind `FeatureCut4` 失败 | 核对切割草图、深度、目标和选择状态 | 返回有效 Feature，切除结果通过 |
| `hole_execution_failed` | 圆草图加 blind `FeatureCut4` 的任一步失败 | 分开记录圆草图与切除子步骤；不切换 Hole Wizard | 孔组合结果和几何审查通过 |
| `feature_result_invalid` | API 未抛异常但返回对象、依赖或重建无效 | 拒绝假成功，补结果和重建校验 | 所有特征结果具备有效标识、顺序和状态 |
| `feature_artifact_missing` | SLDPRT、STEP 或报告缺失/为空/陈旧 | 修复保存、导出或报告；禁止复用历史 latest | 三个当次输出存在、非空且运行标识一致 |
| `feature_api_unverified` | API 仅为 `diagnostic_candidate` / `unverified` | 停止生产，先运行和审查专用 diagnostic | 同 Adapter/版本/参数轮廓证据为 `verified` |

### 修复顺序

1. 先确认 Handler 未直接访问 COM，并验证 `ISolidWorksFeatureAdapter` 注入。
2. 输入或 Registry 失败先在无 COM 层修复。
3. API 候选未验证时以 `feature_api_unverified` 停止，不得主 Worker 盲试。
4. Adapter 执行问题进入专用 diagnostic，并只读取 `output/solidworks/features/<timestamp>/` 当次报告和产物。
5. diagnostic 通过后先完成 evidence 审查，再从 `run-cad-workflow` 重跑最终主流程。

禁止用 `SimpleHole2` / Hole Wizard 修孔，禁止 diagnostic 替代 QualityGate，禁止空 Feature 或空文件成功，禁止进入 V2.0-D。

### 旧 run 假成功修复结论

`20260730_073759_9143941` 证明“Feature 非空 + 重建通过 + 文件非空”仍可能是假成功：Cut/Hole 的人工复核未见孔。遇到同类情况必须返回 `feature_result_invalid`，并增加特征前后体积变化、特征树和视图检查，不能保留原 evidence 为 `verified`。

修复后采用 `20260730_085830_6592380`：Cut 和 Hole 均产生严格递减体积，特征树包含两个 `ICE`，等轴测/俯视确认两个孔。该结果只关闭精确 blind profile 的假成功问题；diagnostic 仍为 `NotDeliverable`，最终关闭条件是 `run-cad-workflow` 主流程通过。

## V2.0-D 重建与真实几何失败修复

先读取同一次 `rebuild_report.json`、`geometry_validation_report.json`、`FeatureGraph` 快照和端到端 `QualityGate` 结果；不得扫描或复用历史产物。记录的 `failure_stage` 必须是下列七项之一，并由失败闭环驱动修复：

| failure_stage | 触发条件 | 修复边界 |
|---|---|---|
| rebuild_failed | SolidWorks 重建失败或存在重建错误 | 修复既有 FeatureGraph 参数映射或已注册 Handler，不直接改 COM 业务逻辑。 |
| geometry_read_failed | 当前受控模型的真实几何无法读取 | 仅在 GeometryReader 的 COM 边界补 API evidence 或读取封装。 |
| bounding_box_invalid | BoundingBox 缺失、退化或明显违反期望 | 检查单位、Body 选择和实际几何；BoundingBox 不是精确尺寸的替代物。 |
| volume_validation_failed | Body 数量或 Volume 不满足期望/单调变化 | 回到产生差异的既有 Feature 及其参数。 |
| parameter_geometry_mismatch | length_mm、diameter_mm 等未反映到真实几何 | 修复 ModelUpdateService 到既有 Sketch/Feature 的映射。 |
| feature_missing_after_rebuild | Sketch、Extrude、Cut 或 Hole 在真实特征树中缺失 | 通过 FeatureHandler 重建，禁止绕过 Handler 或新增 Feature 类型。 |
| geometry_report_failed | 几何报告缺失、不可解析、字段不完整或最终状态失败 | 修复报告契约和 QualityGate 输入，禁止用文件存在替代报告。 |

四孔验收必须逐个证明四个真实孔，不得把旧 V2.0-C 的两个 ICE、单圆草图、文件名或计数字段说成四孔成功。若既有受证 profile 不足以表达目标，保持 feature_api_unverified 并停止，不得静默扩展到 pattern、任意面、through_all、mid_plane、SimpleHole2 或 Hole Wizard。

修复后的最终回归只能运行 run-cad-workflow --input examples/parameter_update_plate.json，并要求 GeometryValidator、Artifact Validator、Reviewer 和 QualityGate 同次通过；V2.0-E 不得绕过这些门禁。

## V2.1-A 夹套失败修复

夹套参数缺失、非有限正数、内径不小于外径或单边壁厚小于 1 mm 时，在 Validator 阶段拒绝，不得连接 SolidWorks。`jacket_profile_create_failed`、`jacket_extrude_failed`、`jacket_inner_cut_failed` 分别定位外圆草图、实体拉伸和 TopPlane 内圆盲切；重建、实测几何或 STEP 内容不满足合同时必须沿对应失败阶段停止并保留失败构建报告。`part_family_api_evidence_insufficient` 表示受控 diagnostic、源码修订或运行时版本尚未恢复，必须重新采集真实证据，不能把历史自由文本改成授权。最终只用 `real_cad_jacket_request.json` 主流程回归。

## V2.1-B 孔失败路由

### 目标、适用范围与输入输出

适用四类孔的前置拒绝、Adapter 执行与几何失败。输入为请求、编译计划、当次报告和证据；输出为直接原因、失败阶段、证据路径、修复和下一步验证。专门手册为 `src/Workers/SolidWorks/Features/Hole/failure_repair.md`。

### 执行步骤与验证标准

按类型 → 参数 → 引用 → API evidence → 独立几何 → QualityGate 排查。`unsupported_hole_type`、`invalid_hole_parameter`、`hole_reference_face_missing` 必须在 Worker 前拒绝；`simple_hole_execution_failed`、`counterbore_execution_failed`、`countersink_execution_failed`、`tapped_hole_execution_failed` 保留实际类型；`hole_geometry_validation_failed` 阻断后续交付；`tapped_hole_api_unverified` 与其他类型的 `feature_api_unverified` 必须在 COM 前失败关闭。

修复输入后重跑四个 dry-run 和负例；涉及 API 时先隔离诊断、再回填准确证据，最后执行 build、test、self-check 与所需完整主流程。通过须有实际读数与同次报告，不能仅凭 Feature 存在。

### 常见失败与禁止事项

孔位越界、数量不符、未知面、盲孔穿底、沉孔/沉头非法比例、螺纹目录或深度错误、缺少测量、证据源码不匹配均须保留原因。禁止静默转普通孔、以 Cut 替代攻丝、改写历史报告、猜 API 参数、绕过质量链或进入 V2.1-C。
