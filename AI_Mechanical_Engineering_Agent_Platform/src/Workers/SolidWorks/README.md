# SolidWorks Worker 说明

本目录是 SolidWorks 执行层边界。V0.9-B 已提供 `FakeSolidWorksWorker` dry-run skeleton；V1.0-A 新增真实执行前的安全边界、环境预检、COM 会话封装和 `RealSolidWorksWorker` 骨架。

当前仍然默认禁止真实 CAD 执行：

- 默认不启动 SolidWorks。
- 默认不调用 COM。
- 默认不调用 `SldWorks.Application`。
- 默认不生成真实 `.SLDPRT`、`.SLDDRW`、`.STEP` 或 PDF 文件。
- 默认不执行真实建模命令。
- 不得复制外部 `solidworks-automation-skill/scripts` 源码。
- 不得把 Python COM 脚本直接塞进 Worker。

## FakeSolidWorksWorker

`FakeSolidWorksWorker` 只生成模拟产物，用于验证平台调度、产物记录、Validator、Reviewer、QualityGate 和 self-check：

- `output/solidworks/artifacts/fake_plate_basic_4holes.SLDPRT.txt`
- `output/solidworks/artifacts/fake_plate_basic_4holes.STEP.txt`
- `output/solidworks/reports/build_report.json`
- `output/solidworks/logs/fake_solidworks_worker.log`

这些文件不是 SolidWorks 原生模型，也不是 STEP 文件，只是 dry-run 产物。

## RealSolidWorksWorker

V1.0-A 的 `RealSolidWorksWorker` 只实现真实执行前边界和连接 smoke test，不实现真实建模。真实连接必须同时满足三项条件：

- `SolidWorksWorkerRequest.AllowRealCadExecution = true`
- `SolidWorksWorkerRequest.DryRun = false`
- 环境变量 `SW_ENABLE_REAL_EXECUTION=true`

如果任一条件不满足，Worker 必须拒绝真实执行并返回结构化 issue：

- `real_cad_execution_not_enabled`
- `dry_run_mode_enabled`
- `missing_user_safety_confirmation`

V1.0-A 只允许以下执行模式：

- `Fake`
- `RealPreflightOnly`
- `RealConnectionSmokeTest`

`RealBuild` 是预留模式，本轮不可用。即使连接 smoke test 成功，也只能设置 `RealCadConnected = true`，不能设置 `RealCadExecuted = true`，因为没有执行建模、保存或导出命令。

## SolidWorksSessionManager

`SolidWorksSessionManager` 使用 late binding 封装 `SldWorks.Application` 连接，不引入 SolidWorks Interop 编译期依赖。它负责：

- 按 `SW_VISIBLE` 控制可见模式。
- 按 `SW_CONNECT_TIMEOUT_SECONDS` 控制连接超时。
- 捕获 COM、超时和连接异常并转为结构化 issue。
- 在 `DisconnectAsync` 中释放 COM 对象。
- 不在构造函数中连接 SolidWorks。

默认 self-check 不会调用 `SolidWorksSessionManager.ConnectAsync`。只有同时设置 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_SMOKE_TEST=true` 时，self-check 才允许尝试真实连接；如果还设置 `SW_STRICT_REAL_SMOKE_TEST=true`，连接失败才会导致 final_status 失败。

## 平台边界

- Agent 只能提出计划和协作建议，不能直接调用 Worker。
- Gateway 不能直接调用 Worker。
- LLM 不能直接调用 Worker。
- Worker 必须通过平台调度边界执行，并保留审计日志。
- 真实 CAD 执行必须经过请求级开关、环境变量级开关和 QualityGate。
- V1.0-B 才允许开始最小真实建模能力。
