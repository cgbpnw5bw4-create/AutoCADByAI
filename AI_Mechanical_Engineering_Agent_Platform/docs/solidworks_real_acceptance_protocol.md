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

## V1.7 主工作流端到端验收

V1.7 通过唯一 CLI 入口接收结构化请求：

```powershell
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_plate_request.json
```

CLI 必须经 Gateway 调用 `chief-engineer`，再由 `ChiefEngineerOrchestrator`、`SequentialWorkflowEngine` 和 `SolidWorksWorkflowRouter` 进入 Worker；CLI 不得直接调用 Worker、Builder 或任何 SmokeRunner。受控 operation 仅为 `build_complete_drawing_package`，且 `part_type` 必须为 `plate_basic_4holes`。

真实执行必须同时满足 `allow_real_cad_execution=true`、`dry_run=false`、`SW_ENABLE_REAL_EXECUTION=true` 与 `SW_REAL_MAIN_WORKFLOW_TEST=true`。缺少任一条件时，V1.7 必须写出 `e2e_execution_report.json`，以 `real_execution_confirmation_missing` 失败关闭；不得回退 Fake Worker 后返回通过。`SW_VISIBLE=true` 仅用于人工观察。

本次运行依次使用已有 Build、Drawing、Dimension 和 TitleBlock Worker 能力。真实阶段源输出仍位于受控 `output/solidworks/real/` 根目录，以便沿用既有 ArtifactValidator；最终同次发布包位于 `output/solidworks/e2e/plate_basic_4holes/<timestamp>/`。发布包仅接收本次 `request_id` 的显式源路径，不扫描历史 latest，也不收集 SmokeRunner 诊断报告作为最终成功依据。

成功包至少包含 `artifacts/plate_basic_4holes.SLDPRT`、`STEP`、`SLDDRW`、`pdf`，以及 `reports/e2e_execution_report.json`、四个阶段报告、`package_quality_report.json`、`release_manifest.json` 和 `latest_real_outputs.md`。任一阶段报告失败、缺失、产物为空、未证明 `real_cad_executed=true`、执行模式不匹配或总体 QualityGate 未通过时，必须令 `all_source_reports_passed=false` 与 `deliverable_status=NotDeliverable`。

真实建模仍要求既有零件与工程图模板可用。若 `SW_TEMPLATE_PART_PATH` 或工程图模板配置缺失，必须以既有可行动 `preflight_failed` / 模板 failure_stage 结束；不得为了通过 V1.7 绕过预检或新增未证实 API。

标题栏的验收语义保持不变：只确认自定义属性写入、读回和重建，不确认 Sheet Format 中字段已经可见渲染。

## V1.7-REAL-AUTH 本地真实执行授权

`run-cad-workflow` 只在项目根目录存在且启用 `config/solidworks.local.json` 时允许真实主流程。该文件必须声明 `real_execution_authorized=true` 与 `execution_authorization_source=LocalDevelopmentProfile`；文件已被 `.gitignore` 排除，参考字段见 `config/solidworks.local.example.json`。

授权配置由 CLI 在创建平台前注入当前进程的 `SW_ENABLE_REAL_EXECUTION=true`、`SW_REAL_MAIN_WORKFLOW_TEST=true`、模板路径和 `SW_VISIBLE`（默认 `true`）。结构化输入不再重复携带 `allow_real_cad_execution` 或 `dry_run`；CLI 仅在有效本地配置下将其转换为真实执行请求。E2E Runner 会再次读取本地配置，单靠环境变量不能绕过该检查。

默认 self-check、CI 和缺少本地授权文件的 CLI 均不得启动 SolidWorks。缺少或无效授权以 `local_execution_authorization_missing` 失败关闭；一旦授权已经通过，后续失败必须保留实际的 `failure_stage`，例如 `preflight_failed`、`solidworks_connection_failed` 或阶段报告中的失败，不能回退为确认缺失。

`e2e_execution_report.json` 必须记录 `real_execution_authorized`、`execution_authorization_source`、`solidworks_launch_attempted`、`solidworks_connected`、`real_worker_invoked` 与 `real_cad_executed`。只有真实 Worker、四阶段 Validator/Reviewer/QualityGate、同次发布包和总体 QualityGate 都通过时才可交付。
