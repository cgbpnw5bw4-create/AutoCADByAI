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

## V1.1 工程图基础视图失败

V1.1 工程图失败必须先读取 `drawing_report.json`，再判断 `failure_stage`。默认 self-check 不应启动 SolidWorks；只有工程图 smoke test 明确开启时才允许真实调用。

| failure_stage | 可能原因 | 首先读取 | 处理方式 |
|---|---|---|---|
| `source_part_missing` | 输入的 `plate_basic_4holes.SLDPRT` 不存在或路径错误 | `drawing_report.json`、self-check 的 `real_drawing_output_directory` | 先确认 V1.0-B 是否生成真实零件，再重新传入绝对路径 |
| `drawing_template_missing` | `SW_TEMPLATE_DRAWING_PATH` 未设置且无法在 SolidWorks 模板目录找到 `.drwdot` | `drawing_report.json`、环境变量 | 要求用户提供工程图模板路径，不能盲建空 Drawing |
| `drawing_document_create_failed` | `NewDocument` 返回空或模板不兼容 | `drawing_report.json`、SolidWorks 日志 | 在 `SolidWorksDrawingSmokeRunner` 中隔离验证模板 |
| `source_part_open_failed` | `OpenDoc6` 打开零件失败 | `drawing_report.json` | 检查 SLDPRT 是否完整、是否被占用、路径是否为绝对路径 |
| `source_part_activate_failed` | `ActivateDoc3` 无法激活零件文档 | `drawing_report.json` | 检查活动文档标题和打开文档列表 |
| `front_view_create_failed` | Front 视图创建失败 | `drawing_report.json`、API evidence | 查证 `CreateDrawViewFromModelView3` 的视图名和坐标 |
| `top_view_create_failed` | Top 视图创建失败 | `drawing_report.json`、API evidence | 查证标准视图名 `*Top` 是否适配当前版本 |
| `right_view_create_failed` | Right 视图创建失败 | `drawing_report.json`、API evidence | 查证标准视图名 `*Right` 和模型路径 |
| `isometric_view_create_failed` | Isometric 视图创建失败 | `drawing_report.json`、API evidence | 查证标准视图名 `*Isometric` |
| `slddrw_save_failed` | 工程图保存失败、路径权限不足或 `SaveAs` 返回失败 | `drawing_report.json` | 检查 `slddrw_path`、文件大小和保存错误 |
| `pdf_export_failed` | PDF 导出失败或导出后文件为空 | `drawing_report.json`、API evidence | 确认 Drawing 是活动文档，优先查证 `IModelDocExtension.SaveAs` 和 PDF export data |
| `drawing_report_write_failed` | 报告写出失败 | 输出目录 | 先修目录和权限，不继续改 SolidWorks API |

工程图 API 不确定时必须进入 API evidence 流程，不允许凭感觉修改长参数或把宏作为生产路径。
