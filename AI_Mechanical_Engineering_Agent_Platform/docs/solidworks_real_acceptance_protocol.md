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

操作者应在结构化输入中明确保留前两项请求确认，并在 PowerShell 中显式设置后两项环境确认：

```powershell
$env:SW_ENABLE_REAL_EXECUTION="true"
$env:SW_REAL_MAIN_WORKFLOW_TEST="true"
$env:SW_VISIBLE="true"
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_plate_request.json
```

本次运行依次使用已有 Build、Drawing、Dimension 和 TitleBlock Worker 能力。真实阶段源输出仍位于受控 `output/solidworks/real/` 根目录，以便沿用既有 ArtifactValidator；最终同次发布包位于 `output/solidworks/e2e/plate_basic_4holes/<timestamp>/`。发布包仅接收本次 `request_id` 的显式源路径，不扫描历史 latest，也不收集 SmokeRunner 诊断报告作为最终成功依据。

成功包至少包含 `artifacts/plate_basic_4holes.SLDPRT`、`STEP`、`SLDDRW`、`pdf`，以及 `reports/e2e_execution_report.json`、四个阶段报告、`package_quality_report.json`、`release_manifest.json` 和 `latest_real_outputs.md`。任一阶段报告失败、缺失、产物为空、未证明 `real_cad_executed=true`、执行模式不匹配或总体 QualityGate 未通过时，必须令 `all_source_reports_passed=false` 与 `deliverable_status=NotDeliverable`。

真实建模仍要求既有零件与工程图模板可用。若 `SW_TEMPLATE_PART_PATH` 或工程图模板配置缺失，必须以既有可行动 `preflight_failed` / 模板 failure_stage 结束；不得为了通过 V1.7 绕过预检或新增未证实 API。

标题栏的验收语义保持不变：只确认自定义属性写入、读回和重建，不确认 Sheet Format 中字段已经可见渲染。

## V1.7-REAL-AUTH 本地真实执行授权

`run-cad-workflow` 只在项目根目录存在且启用 `config/solidworks.local.json` 时允许真实主流程。该文件必须声明 `real_execution_authorized=true` 与 `execution_authorization_source=LocalDevelopmentProfile`；文件已被 `.gitignore` 排除，参考字段见 `config/solidworks.local.example.json`。

授权配置由 CLI 在创建平台前仅注入模板路径、超时、授权审计字段，并仅在操作者未设置时提供 `SW_VISIBLE` 默认值；它不会设置 `SW_ENABLE_REAL_EXECUTION` 或 `SW_REAL_MAIN_WORKFLOW_TEST`。结构化输入必须明确携带 `allow_real_cad_execution=true` 与 `dry_run=false`，CLI 会原样传递这两个请求确认。E2E Runner 会再次读取本地配置，因此本地授权、结构化输入和操作者显式设置的两个核心环境变量缺一不可。

默认 self-check、CI 和缺少本地授权文件的 CLI 均不得启动 SolidWorks。缺少或无效授权以 `local_execution_authorization_missing` 失败关闭；一旦授权已经通过，后续失败必须保留实际的 `failure_stage`，例如 `preflight_failed`、`solidworks_connection_failed` 或阶段报告中的失败，不能回退为确认缺失。

`e2e_execution_report.json` 必须记录 `real_execution_authorized`、`execution_authorization_source`、`solidworks_launch_attempted`、`solidworks_connected`、`real_worker_invoked` 与 `real_cad_executed`。只有真实 Worker、四阶段 Validator/Reviewer/QualityGate、同次发布包和总体 QualityGate 都通过时才可交付。

## V1.9 Phase 1 多零件族真实 build-only 验收

### 目标与适用范围

V1.9 Phase 1 将 `flange_basic` 和 `shaft_basic` 接入真实 SolidWorks 主工作流程，但本阶段的验收合同仅为 build-only：真实生成零件与 STEP，通过产物校验、工程复审、质量门禁，再生成同次发布包。这两个零件族不自动生成工程图、尺寸、标题栏或 PDF。

`plate_basic_4holes` 不改为 build-only，它必须保留 V1.7 已验收的完整工程图包回归。本阶段完成后停在 V1.9 Phase 1，不进入 V2.0。

### 唯一最终执行链

```text
结构化输入
→ Gateway / chief-engineer
→ ChiefEngineerOrchestrator
→ WorkflowEngine
→ SolidWorksWorkflowRouter
→ PartTypeRegistry
→ PartFamilyBuilderRegistry
→ RealSolidWorksWorker
→ ArtifactValidator
→ Reviewer
→ QualityGate
→ build-only ReleasePackage
```

最终验收必须从结构化入口经 Gateway 和公开 `chief-engineer` 进入上述完整链。直接调用 `FlangeFeatureBuilder`、`ShaftFeatureBuilder`、任何独立 Builder 或 SmokeRunner，都只能作为诊断或开发证据，不能作为最终验收结论。

### 三层授权和本地配置

真实执行必须同时通过三层授权：

1. 请求层：`allow_real_cad_execution=true` 且 `dry_run=false`。
2. 本地授权层：未提交的 `config/solidworks.local.json` 声明 `real_execution_authorized=true` 且 `execution_authorization_source=LocalDevelopmentProfile`。
3. 环境层：`SW_ENABLE_REAL_EXECUTION=true` 且 `SW_REAL_MAIN_WORKFLOW_TEST=true`。

任一层缺失时必须失败关闭，不得回退 Fake Worker 后声称真实成功。默认 self-check 不应用本地 profile、不设置环境授权、不连接 COM，也不启动 SolidWorks。

### 真实执行顺序

SolidWorks COM 执行在全局范围内串行，不允许两个真实零件族任务并发操作同一应用会话。首次阶段验收顺序固定为：

1. `flange_basic`
2. `shaft_basic`

前一个任务未完成会话释放、报告写入和质量门禁裁决时，后一个任务不得开始连接。

### build-only 发布包合同

每次运行的最终目录为：

```text
output/solidworks/e2e/<part_type>/<timestamp>/
```

`flange_basic` 和 `shaft_basic` 的最小验收包必须包含：

```text
artifacts/<part_type>.SLDPRT
artifacts/<part_type>.STEP
reports/build_report.json
reports/e2e_execution_report.json
release_manifest.json
```

可以额外生成 `package_quality_report.json` 和中文摘要，但不能用这些附加文件代替必需产物或必需报告。`release_manifest.json` 只能收集当次 request 的显式源集，不扫描历史 latest。

`build_report.json` 必须至少可证明 `part_type`、真实模式、执行模式、真实授权请求、SolidWorks 连接、保存、STEP 导出、API evidence、`failure_stage` 和最终状态。`e2e_execution_report.json` 必须证明完整平台调用链、三层授权、真实 Worker、ArtifactValidator、Reviewer、QualityGate 和发布包结论。

### 失败阶段

V1.9 Phase 1 必须使用下列稳定 `failure_stage`：

```text
part_family_api_evidence_insufficient
flange_profile_create_failed
flange_extrude_failed
flange_inner_cut_failed
flange_bolt_holes_failed
shaft_profile_create_failed
shaft_revolve_failed
shaft_step_feature_failed
part_save_failed
step_export_failed
artifact_validation_failed
quality_gate_rejected
```

任一阶段失败都必须保留直接原因、API 或平台证据、输出路径、修复策略和下一步验证命令。

### API 证据门槛

- `flange_basic` 的候选主路径为：外圆 `CreateCircle` 与 `FeatureExtrusion2`；中心孔使用独立活动草图 `FeatureCut4`；螺栓孔在单草图中创建全部圆，再用 `FeatureCut4` 一次切除。拒绝 `HoleWizard` 和圆周阵列 API。
- `shaft_basic` 的当前项目合同固定为：`CreateLine` 创建闭合轴向轮廓，`CreateCenterLine` 创建旋转中心线，中心线选择标记为 `16`，再以 `FeatureRevolve2` 完成 360° 旋转。偏移多段拉伸只记入 backlog / 拒绝策略，本轮不混用。

Phase 1 入场时两族都只能声称“证据足以进入独立 diagnostic”；该限制现已由下述 Phase 2 真实运行证据关闭。

### 自检字段

```text
flange_real_builder_implemented
shaft_real_builder_implemented
flange_real_workflow_supported
shaft_real_workflow_supported
flange_real_workflow_default_disabled
shaft_real_workflow_default_disabled
flange_api_evidence_documented
shaft_api_evidence_documented
flange_artifact_validation_supported
shaft_artifact_validation_supported
plate_part_family_regression_passed
no_large_part_type_switch
all_part_families_use_registry
v1_9_version_stage_documented
markdown_chinese_check_passed
```

### 验收标准与禁止事项

默认 build、test 和 self-check 必须通过，且 self-check 不连接 COM。上述自检字段必须全部为 `true`。真实验收时，按 flange 再 shaft 的顺序分别运行主工作流程，并通过人工打开 SLDPRT / STEP 检查。每族只有在当次报告、非空产物、Validator、Reviewer、QualityGate 和发布包均通过后，才能标记 build-only 可交付。

禁止 flange / shaft 自动工程图，禁止并发真实 COM，禁止绕过 Registry、Worker 或 QualityGate，禁止用 Builder / SmokeRunner 代替最终验收，禁止在本轮进入 V2.0。

## V1.9 Phase 2 验收证据回填

### 验收结论

Phase 1 中“尚待回填”的状态已由 2026-07-20 的真实运行证据取代。diagnostic 只负责证明专用 API 路径和几何候选；最终结论来自随后按顺序执行的 CLI 主工作流程。

| 零件族 | 专用 diagnostic | 视觉与特征证据 | CLI 主流程结论 |
|---|---|---|---|
| `flange_basic` | `CandidatePassed`；SLDPRT 91751 字节，STEP 48876 字节 | 规则审查 100 分且通过；特征树含一个 `Extrusion` 和两个 `ICE`；四视图确认中心孔及 6 个螺栓孔 | `Passed`、`Deliverable`、QualityGate `Passed` |
| `shaft_basic` | `CandidatePassed`；SLDPRT 91716 字节，STEP 23323 字节 | 规则审查 100 分且通过；特征树含 `Revolution`；四视图确认直径 40 的主体及直径 32、直径 24 的两级台阶 | `Passed`、`Deliverable`、QualityGate `Passed` |
| `plate_basic_4holes` | 沿用既有真实建模能力 | 完整 Drawing、Dimension、TitleBlock 与 PDF 回归 | `Passed`、`Deliverable`、QualityGate `Passed` |

证据目录如下：

```text
output/solidworks/diagnostics/v1_9/flange_basic/20260720_081331_449_b538c0c180d44bc6a3007a34e1bd1c0f/
output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/
output/solidworks/diagnostics/v1_9/shaft_basic/20260720_081653_181_b0b4a7315e994226b8361ee551be7e6b/
output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/
output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260720_082027_397-bd86bc56b48349c69db5f8173c1b3d85/
```

### 修复记录

flange 首次 diagnostic 在保存零件时返回 `part_save_failed`。修复复用 plate 已验证的 `SaveAs3` 主路径和 `SaveAs` 回退路径；再次运行后得到非空 SLDPRT、STEP 与 `CandidatePassed` evidence。

首次 CLI 主流程在发布包阶段遇到瞬时源文件哈希读锁并返回 `artifact_copy_failed`。修复后的判定仍要求复制成功并完成目标文件校验；只有目标校验通过时，已经恢复的源文件读锁才降级为 warning。最终重跑通过，不能把这条降级规则用于掩盖复制失败、目标缺失或校验失败。

### 剩余证据边界

两份 diagnostic 的 `geometry_body_count_status` 和 `theoretical_volume_status` 仍为 `NotVerified`。本阶段以专用 API 返回、特征树、四视图人工检查、非空 SLDPRT / STEP、ArtifactValidator、Reviewer、同次 CLI 主工作流程和 QualityGate 共同构成基础零件族验收证据；body count 与理论体积自动核验记入 Improvements backlog，不阻塞 V1.9 基础族验收。

本回填不改变 flange / shaft 的 build-only 边界，不授权自动工程图，也不进入 V2.0。
