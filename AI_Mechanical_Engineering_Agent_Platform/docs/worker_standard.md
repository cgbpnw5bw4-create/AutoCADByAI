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
