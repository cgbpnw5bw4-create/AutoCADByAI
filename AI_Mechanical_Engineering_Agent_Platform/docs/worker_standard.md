# Worker Standard

A Worker is the execution boundary for external systems.

Workers are responsible for future calls to:

- SolidWorks API, SDK or COM
- AutoCAD API, SDK or COM
- DWG/DXF parsers
- industrial software MCP servers
- local build, export or analysis tools

Workers expose `IWorker`:

- `Name`
- `TargetSystem`
- `ExecuteAsync(WorkerInput input)`

Workers return `WorkerOutput` with:

- status
- generated artifacts
- execution log
- issues

Agents must not directly call CAD APIs, SDKs or COM objects. They prepare structured plans and route execution through Workers.
