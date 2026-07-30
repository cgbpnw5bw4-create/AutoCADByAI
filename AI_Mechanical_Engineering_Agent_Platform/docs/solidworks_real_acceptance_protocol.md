# SolidWorks 真实主工作流验收协议

## 当前规则

V2.0-SW-DEFAULT-ON 起，本地交互式 `run-cad-workflow` 默认启用真实 SolidWorks：

- `RealExecutionDefaultEnabled=true`。
- `dry_run=false`。
- `VisibleModeDefault=true`，即未设置 `SW_VISIBLE` 时显示 SolidWorks。
- 不再要求 `SW_ENABLE_REAL_EXECUTION=true`。
- 不再要求请求携带 `allow_real_cad_execution=true`。
- `config/solidworks.local.json` 只提供可选模板、可见性和超时，不承担授权职责。

以下任一条件成立时，真实执行必须在连接前关闭：

- 请求显式 `dry_run=true`。
- `SW_DISABLE_REAL_EXECUTION=true`。
- 当前为 CI 环境。
- 当前为单元测试环境。
- `SW_FORCE_FAKE_WORKER=true`。

`SW_VISIBLE=false` 只把本地真实执行切换为后台模式，不会关闭真实执行。

## 执行环境结构闸门

`RealSolidWorksWorker` 在创建或连接 COM 应用之前必须完成只读环境探测。探测不实例化 SolidWorks，并至少确认：

- 当前操作系统是 Windows。
- 当前进程处于交互式桌面会话。
- `SldWorks.Application` COM ProgID 已注册。

任一条件不满足时，主流程必须以 `failure_stage=real_execution_environment_unavailable` 失败，保留具体 issue，且 COM 连接尝试次数为 0。不得因环境不可用自动回退 Fake Worker，也不得把未执行真实 CAD 的结果写成 Passed。

## 不可绕过的主流程

最终验收必须从 CLI 入口开始，并经过：

```text
结构化输入
→ ChiefEngineerOrchestrator
→ WorkflowEngine
→ SolidWorksWorkflowRouter
→ PartTypeRegistry
→ PartFamilyBuilder
→ RealSolidWorksWorker
→ ArtifactValidator
→ Reviewer
→ QualityGate
→ ReleasePackage
```

Agent、Gateway、LLM、Builder 和 SmokeRunner 都不能直接替代该链路。独立 diagnostic 只用于 API 查证和故障隔离，不能作为最终验收。

## 本地配置

可复制 `config/solidworks.local.example.json` 为未提交的 `config/solidworks.local.json`。支持：

- `visible`。
- `template_part_path`。
- `template_drawing_path`。
- `connect_timeout_seconds`。
- `execution_timeout_seconds`。

配置不存在时不阻止本地交互式真实执行；但真实建模需要的模板路径仍必须能够被运行环境解析，否则以 `preflight_failed` 或更具体阶段失败。

## 环境变量

主流程只使用以下执行策略变量：

```powershell
$env:SW_DISABLE_REAL_EXECUTION="true" # 显式关闭
$env:SW_VISIBLE="false"               # 后台运行
$env:SW_FORCE_FAKE_WORKER="true"      # 强制 Fake Worker
```

本地可见真实执行不需要设置任何启用变量：

```powershell
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_plate_request.json
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_flange_request.json
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_shaft_request.json
```

## 产物与真值

真实零件族验收目录：

```text
output/solidworks/e2e/<part_type>/<run_id>/
├── artifacts/
│   ├── <part_type>.SLDPRT
│   └── <part_type>.STEP
├── reports/
│   ├── build_report.json
│   └── e2e_execution_report.json
└── release_manifest.json
```

plate 完整工程图包还应包含 SLDDRW、PDF 和对应阶段报告。

只有以下条件全部成立时才可声明真实验收通过：

- `real_cad_connected=true`。
- `real_cad_executed=true`。
- SLDPRT、STEP、SLDDRW、PDF 和要求的报告必须按本次请求逐项检查；要求生成的文件必须存在且 `size_bytes > 0`。
- ArtifactValidator 与 Reviewer 通过。
- QualityGate 为 `Passed`。
- `all_source_reports_passed=true`。
- `deliverable_status=Deliverable`。
- `final_status=Passed`。

Fake Worker 的成功、diagnostic 的候选成功、历史 latest 文件或仅文件存在都不能替代上述证据。

## 人工视觉检查

文件非空只证明保存或导出产生了字节，不证明零件和图纸可交付。真实主流程完成后，验收人必须打开同次运行目录中的产物检查：

- `plate_basic_4holes.SLDPRT`：板长、宽、厚度、四孔数量、孔径和孔位与结构化输入一致。
- `plate_basic_4holes.SLDDRW` 与 PDF：前视图、俯视图、右视图、等轴测图四个视图均存在，视图方向、比例和轮廓可辨认。
- 尺寸：必做尺寸可见、数值与模型一致，不重叠到无法读取。
- 标题栏：零件名称、材料、图号等已写字段可见，且报告中的属性回读没有不一致。
- `flange_basic` 与 `shaft_basic`：按各自四视图证据检查关键孔、台阶或旋转轮廓；Phase 2 证据不能由 diagnostic 的 `CandidatePassed` 单独替代。

人工检查结论和本次输出路径应一并交给用户。未完成人工视觉检查时，可以报告“主流程技术门禁通过”，但不能声称视觉验收已经完成。

## 失败处理

禁用策略导致的失败使用明确原因，例如：

- `dry_run_real_execution_disabled`。
- `real_execution_disabled`。
- `real_execution_environment_unavailable`。

真实执行开始后必须保留实际阶段，例如连接、模板、特征创建、保存、STEP 导出、artifact validation 或 QualityGate 失败，不得改写为旧式确认缺失。

COM 调用必须受连接超时、执行超时、取消令牌和全局串行锁保护。失败后先读取最新报告和 API evidence；API 证据不足时进入独立 diagnostic，不得盲改主 Worker。

## self-check 和测试

本地交互式主流程默认真实执行，不代表所有命令都可启动 CAD：

- `self-check` 不启动 SolidWorks。
- CI 不启动 SolidWorks。
- 单元测试不启动 SolidWorks。
- dry-run 不启动 SolidWorks。

必须验证：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

self-check 必须包含：

- `solidworks_local_interactive_default_enabled`。
- `solidworks_disable_env_supported`。
- `solidworks_ci_execution_disabled`。
- `solidworks_unit_test_execution_disabled`。
- `solidworks_dry_run_disables_real_execution`。
- `solidworks_visible_default_true`。
- `solidworks_execution_environment_probe_supported`。
- `legacy_enable_flag_not_required`。
- `legacy_request_confirmation_not_required`。
- `markdown_chinese_check_passed`。
