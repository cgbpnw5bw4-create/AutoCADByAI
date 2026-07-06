# CADModeling 审查清单

## Blockers

- 默认 self-check 启动真实 CAD。
- `Agent`、`Gateway` 或 `LLM` 直接调用 Worker。
- `Skill` 直接执行 CAD 操作。
- COM 类型泄漏到 Contracts、PlatformCore 或 QualityGate。
- 失败只记录 `Failed`，没有可行动 `failure_stage`。
- API 失败没有 evidence report。
- 复制第三方 `scripts` 源码。
- V1.2 尺寸标注默认 self-check 启动 SolidWorks。
- V1.2 尺寸标注没有 `dimension_report.json` 或没有可行动 `failure_stage`。
- V1.3 标题栏测试默认 self-check 启动 SolidWorks。
- V1.3 标题栏没有 `title_block_report.json` 或没有可行动 `failure_stage`。
- V1.3 借标题栏扩展到 BOM、装配图、明细栏、复杂国标模板、公差系统、形位公差、表面粗糙度、批量出图或 V1.4。
- Markdown 中文检查失败。

## V1.3 标题栏语义边界

- `title_block_report.json` 必须保留 `title_block_population_strategy=custom_properties_only`。
- `title_block_fields_verified_in_sheet_format` 必须保持 `false`，直到实现 Sheet Format note / `$PRP` 可见渲染校验。
- 不得把 `custom_properties_written=true` 或 `title_block_updated=true` 解释为国标标题栏格子已经可见填充。

## Improvements

- evidence 报告可以更细化候选策略。
- diagnostic report 可以增加更多中间对象状态。
- Reviewer 规则可以增加工程合理性检查。
- V1.2 后续可研究关联孔中心尺寸，但必须先补 `IView.GetVisibleEntities2` 和实体选择证据。
- V1.3 后续可研究模板字段映射和国标标题栏样式，但必须先补官方 API 证据和模板兼容测试。

## 进入下一阶段条件

必须通过 build、test、self-check。涉及真实 CAD 的能力必须有默认关闭开关、诊断 Runner、artifact validation 和 QualityGate 记录。

V1.2 还必须确认 `solidworks_real_drawing_dimensions_implemented`、`solidworks_real_drawing_dimensions_not_called_in_default_self_check`、`solidworks_drawing_dimension_failure_stage_actionable`、`solidworks_drawing_dimension_api_evidence_documented` 和 `solidworks_drawing_dimension_failure_repair_documented` 为 true。

V1.3 还必须确认 `solidworks_real_drawing_title_block_implemented`、`solidworks_real_drawing_title_block_not_called_in_default_self_check`、`solidworks_drawing_title_block_failure_stage_actionable`、`solidworks_drawing_title_block_api_evidence_documented`、`solidworks_drawing_title_block_failure_repair_documented` 和 `v1_3_version_stage_documented` 为 true。
