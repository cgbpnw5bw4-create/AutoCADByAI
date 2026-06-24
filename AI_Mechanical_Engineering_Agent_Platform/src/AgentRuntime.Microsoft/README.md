# AgentRuntime.Microsoft

`AgentRuntime.Microsoft` is the only project allowed to reference Microsoft Agent Framework packages.

The rest of the platform keeps using the platform-owned contracts:

- `IAgent`
- `AgentContext`
- `AgentOutput`
- `WorkflowStep`
- `WorkflowExecutionResult`
- `ISkill`
- `IWorker`

No Microsoft Agent Framework type should appear in `AgentContracts`, `PlatformCore`, `ModuleContracts`, `SkillContracts`, `WorkerContracts`, `DomainSchemas`, `QualityGate`, or CAD Worker projects.

## Runtime Modes

The current default runtime mode is `Mock`.

### MockRuntime

Mock runtime is used for self-check and local platform validation when no real model provider, endpoint, or API key is configured.

It can create the six platform agents:

- `chief-engineer` as `Public`
- `mechanical-designer` as `Internal`
- `cad-modeler` as `Internal`
- `drawing-engineer` as `Internal`
- `drawing-reviewer` as `Internal`
- `error-diagnosis` as `Internal`

Mock runtime returns deterministic `AgentOutput` and does not call any external model or CAD system.

### MicrosoftRuntime

Microsoft runtime is reserved for future real Microsoft Agent Framework execution.

This project references `Microsoft.Agents.AI`, but V0.3 does not hard-code provider credentials, model names, API keys, or CAD tool calls. If a real Microsoft runtime invoker is not configured, the adapter returns a clear fallback `AgentOutput` instead of leaking framework-specific types.

## Components

- `MicrosoftAgentAdapter`: wraps runtime-backed behavior as platform `IAgent`.
- `AgentFactory`: creates mock or Microsoft runtime agents from runtime manifests.
- `MicrosoftWorkflowRuntime`: wraps sequential workflow execution using platform workflow contracts.
- `ToolBridge`: maps future runtime tool calls into platform `Skill` or `Worker` names and converts outputs into runtime messages.

## Boundaries

Agents must not directly call CAD Workers. CAD execution must continue to go through platform `WorkerContracts`, module boundaries, and `QualityGate`.

`ToolBridge` is only a mapping layer in V0.3. It does not execute SolidWorks, AutoCAD, COM, SDK, API, or MCP calls.
