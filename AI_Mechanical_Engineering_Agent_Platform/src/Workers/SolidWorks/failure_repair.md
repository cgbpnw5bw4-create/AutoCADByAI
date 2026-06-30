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

## 保存失败

检查 `sldprt_save_*` 字段、输出路径、权限和 `SaveAs` 返回值。不能在 SLDPRT 缺失时返回 Passed。

## STEP 导出失败

检查 `ActiveDoc`、`ActivateDoc`、`ClearSelection2`、`step_export_*` 字段和 STEP 文件大小。导出前必须确保目标文档为活动文档。

## 报告失败

如果 `build_report.json` 或 `diagnostic_report.json` 未写出，先修输出路径和权限，不要继续 CAD API 调试。
