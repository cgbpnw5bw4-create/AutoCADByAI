# CADModeling API 证据规则

## 基本原则

- 不得臆造 API。
- 优先查官方 `SolidWorks API Help`。
- 其次查本地 SDK、宏录制和已验证诊断报告。
- 再参考 `references/external/solidworks-automation-skill-analysis.md`。
- 如果用户手动放入 `references/external/solidworks-automation-skill`，只能只读分析。
- 不能复制 `scripts` 源码。
- 不能把第三方 Python 脚本作为生产路径。
- 需要生成 `ApiEvidenceReport`。

## 证据字段

`ApiEvidenceReport` 必须记录：

- `official_api_sources`
- `local_reference_sources`
- `third_party_reference_sources`
- `extracted_api_candidates`
- `selected_api_strategy`
- `rejected_strategies`
- `final_recommendation`

## 当前重点 API

- `SelectByID2`
- `InsertSketch`
- `CreateCornerRectangle` 或等价矩形草图 API
- `CreateCircleByRadius`
- `FeatureExtrusion`、`FeatureExtrusion2` 或等价拉伸 API
- `FeatureCut3`、`FeatureCut4` 或等价切除 API
- `SaveAs`、`SaveAs2`、`SaveAs3`
- `ActivateDoc`、`ActiveDoc`
- `ClearSelection2`

## 使用边界

证据可以指导封装重写，但不能绕过 `Worker`、`Validator`、`Reviewer` 和 `QualityGate`。未验证 API 不能直接进入主 Worker。
