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

真实 SolidWorks 执行必须等到 V1.0 的 `RealSolidWorksWorker`，并且只有在平台流程显式设置 `allow_real_cad_execution=true` 时才允许进入真实执行路径。默认值必须为 `false`。即使未来接入真实执行，Worker 仍必须通过平台调度、审计日志、Validator、Reviewer 和 `QualityGate`，不得被 Agent、Gateway 或 LLM 直接调用。
