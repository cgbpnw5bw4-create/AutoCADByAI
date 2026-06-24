# AgentRuntime.Microsoft

This project is the only place where Microsoft Agent Framework integration should be added.

Current status:

- no Microsoft Agent Framework package is referenced yet
- `MicrosoftAgentAdapter` wraps a platform `IAgent`
- `MicrosoftWorkflowRuntime` delegates to the platform `SequentialWorkflowEngine`
- `ToolBridge` converts runtime tool calls into platform `Skill` or `Worker` calls

Future integration path:

1. Add the official Microsoft Agent Framework package here only.
2. Map Microsoft runtime agents to `IAgent` through `MicrosoftAgentAdapter`.
3. Map Microsoft orchestration/workflow concepts to platform workflow abstractions.
4. Keep `AgentContracts`, `PlatformCore`, modules and workers free of direct Microsoft runtime APIs.
