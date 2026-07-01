# SolidWorks Worker 失败修复说明

## 连接失败

检查 `SW_ENABLE_REAL_EXECUTION`、COM 注册、SolidWorks 是否安装和 `SolidWorksSessionManager` 日志。默认 self-check 不要求连接成功。

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
