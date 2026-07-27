# Worker 标准

Worker 是外部系统执行边界。

Worker 未来负责调用：

- SolidWorks API、SDK 或 COM。
- AutoCAD API、SDK 或 COM。
- DWG / DXF 解析器。
- 工业软件 MCP Server。
- 本地构建、导出或分析工具。

Worker 暴露 `IWorker`：

- `Name`
- `TargetSystem`
- `ExecuteAsync(WorkerInput input)`

Worker 返回 `WorkerOutput`，其中包含：

- 状态。
- 生成的 artifacts。
- 执行日志。
- issues。

Agent 不得直接调用 CAD API、SDK 或 COM 对象。Agent 只准备结构化计划，并通过 Worker 边界执行。

## SolidWorks dry-run Worker

V0.9-B 的 `FakeSolidWorksWorker` 只用于验证平台调度、产物记录和质量门禁。它不启动 SolidWorks，不调用 COM，不调用 `SldWorks.Application`，也不生成真实 CAD 文件。

真实 SolidWorks 执行由 `RealSolidWorksWorker` 承担。V2.0 本地交互式平台流程默认进入真实执行路径，不再要求 `allow_real_cad_execution`；Worker 仍必须通过平台调度、审计日志、Validator、Reviewer 和 `QualityGate`，不得被 Agent、Gateway 或 LLM 直接调用。

## 真实 CAD Worker 安全开关

V1.0-A 的 `RealSolidWorksWorker` 只建立真实执行前边界，不做真实建模。真实连接必须同时满足请求级和环境级开关：

- 请求级：`SolidWorksWorkerRequest.DryRun = false`。
- 环境级：未设置 `SW_DISABLE_REAL_EXECUTION=true`，且不是 CI、单元测试或强制 Fake Worker。
- 环境级：`SW_TEMPLATE_PART_PATH` 指向有效的 SolidWorks 零件模板。

本地交互式默认 `RealExecutionDefaultEnabled=true`、`SW_VISIBLE=true`、`SW_CONNECT_TIMEOUT_SECONDS=30`。禁用条件满足时，Worker 必须在连接前返回结构化 issue，并保持 `RealCadExecuted=false`。

V1.0-A 只允许 `RealPreflightOnly` 和 `RealConnectionSmokeTest`，不允许 `RealBuild`。即使连接 smoke test 成功，也只能说明 `RealCadConnected = true`，不能说明执行过建模、保存或导出命令。

真实 CAD Worker 仍必须经过 Workflow、Validator、Reviewer、AuditLog 和 `QualityGate`，不得被 Agent、Gateway 或 LLM 直接调用。

## V1.0-B 受控真实构建

V1.0-B 只开放一个最小真实构建模式：`RealBuildPlateBasic4Holes`。它不是通用 `RealBuild`，也不是自然语言到任意模型的执行器。Worker 只能处理 `BuildPlan.PartType = plate_basic_4holes`，输出真实 `.SLDPRT`、`.STEP` 和 `build_report.json`。

真实构建仍必须同时满足：

- 请求级：`SolidWorksWorkerRequest.DryRun = false`。
- 运行策略：未被 `SW_DISABLE_REAL_EXECUTION`、CI、单元测试或 `SW_FORCE_FAKE_WORKER` 禁用。

self-check 默认不执行真实构建。只有设置 `SW_REAL_BUILD_SMOKE_TEST=true` 时才允许尝试；只有再设置 `SW_STRICT_REAL_BUILD_TEST=true` 时，真实构建失败才会影响 `final_status`。未开启这些开关时，平台必须继续使用 dry-run 和 mock 路径。

`ConnectionSmokeTestOnly` 仅用于连接 smoke test，不能被当作建模完成。只有 `RealBuildPlateBasic4Holes` 成功保存和导出产物后，`RealCadExecuted` 才能为 `true`。
