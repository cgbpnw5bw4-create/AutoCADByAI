# SolidWorks 真实产物验收协议

## 目标

本协议定义 `V1.6-TEST-A` 的真实 CAD 产物验收边界。它用于确认已生成文件、阶段报告和发布包状态彼此一致，不允许用文件名、源码字符串或陈旧的 `Passed` 报告替代运行证据。

## 安全开关

真实 CAD 执行默认关闭。进入真实执行必须同时满足：

1. 请求包含 `allow_real_cad_execution=true`。
2. 请求包含 `dry_run=false`。
3. 环境变量 `SW_ENABLE_REAL_EXECUTION=true`。

工程图、尺寸和标题栏 smoke test 还必须分别设置对应的专项环境变量。默认 `self-check` 不设置这些变量，也不得启动 SolidWorks。

## 四个真实模式

| 模式 | 专项开关 | 主要产物 | 必需报告 |
|---|---|---|---|
| 零件建模 | `SW_REAL_BUILD_SMOKE_TEST=true` | `plate_basic_4holes.SLDPRT`、`plate_basic_4holes.STEP` | `build_report.json`、`diagnostic_report.json` |
| 基础工程图 | `SW_REAL_DRAWING_SMOKE_TEST=true` | `plate_basic_4holes.SLDDRW`、`plate_basic_4holes.pdf` | `drawing_report.json` |
| 基础尺寸 | `SW_REAL_DRAWING_DIMENSION_SMOKE_TEST=true` | `plate_basic_4holes_dimensioned.SLDDRW`、`plate_basic_4holes_dimensioned.pdf` | `dimension_report.json` |
| 标题栏信息 | `SW_REAL_DRAWING_TITLE_BLOCK_SMOKE_TEST=true` | `plate_basic_4holes_title_block.SLDDRW`、`plate_basic_4holes_title_block.pdf` | `title_block_report.json` |

发布包只收集上述产物，不属于新的 CAD 模式，也不得启动 SolidWorks。发布包必须生成 `package_quality_report.json`，并以 `deliverable_status` 表示最终是否可交付。

## 证据一致性

- 真实产物必须存在且大小大于零。
- 每个阶段报告必须明确包含 `final_status`；失败时必须包含可行动的 `failure_stage`。
- 任一源报告缺失、不可读或不是 `Passed` 时，`all_source_reports_passed` 必须为 `false`。
- 任一必需产物缺失或为空时，`deliverable_status` 必须为 `NotDeliverable`。
- `package_quality_report.json` 只能收紧上游结论，不能把失败或缺失的源报告放宽为可交付。
- `real_acceptance_report.json` 和平台 self-check 报告必须包含 `schema_version`、`run_id`、`generated_at` 与 `source_revision`。工作区存在已跟踪修改时，`source_revision` 必须带 `-dirty`，避免把不可复现状态误认成干净提交。
- `source_revision`、`schema_version` 或必需字段不匹配的报告不得作为当前运行的通过证据。

## 人工验收清单

1. 确认真实执行开关由操作者显式设置，且运行完成后已关闭。
2. 核对四个模式的产物路径均位于预期输出目录，没有覆盖源模板或用户输入文件。
3. 核对文件存在且非空，并确认报告中的路径与实际文件一致。
4. 核对所有源报告的 `final_status`、`failure_stage` 与发布包结论没有矛盾。
5. 对 `SLDPRT`、`STEP`、`SLDDRW` 和 PDF 做人工打开检查；未经人工检查不得把“文件存在”表述为几何或版面已验收。
6. 记录本次 `run_id`、`source_revision` 和人工验收结论，确保后续可以追踪到同一次运行。

## 验证命令

默认验证不得启动真实 CAD：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

真实 smoke test 仅在操作者显式设置本协议所列开关后手动运行。任何失败都必须保留对应报告并进入 `src/Workers/SolidWorks/failure_repair.md` 描述的修复流程。
