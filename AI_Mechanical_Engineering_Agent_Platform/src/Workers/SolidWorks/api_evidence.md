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
