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

It can create the platform agents:

- `chief-engineer` as `Public`
- `mechanical-designer` as `Internal`
- `cad-modeler` as `Internal`
- `drawing-engineer` as `Internal`
- `drawing-reviewer` as `Internal`
- `code-engineer` as `Internal`
- `code-reviewer` as `Internal`
- `error-diagnosis` as `Internal`

Mock runtime returns deterministic `AgentOutput` and does not call any external model or CAD system.

### MicrosoftRuntime

V0.7 enables real runtime only for `chief-engineer`.

This project references `Microsoft.Agents.AI`, but provider credentials, model names and endpoints are read only from environment variables:

- `AI_AGENT_RUNTIME_MODE`
- `AI_PROVIDER`
- `AI_MODEL`
- `AI_API_KEY`
- `AI_BASE_URL`
- `AI_TEMPERATURE`
- `AI_TIMEOUT_SECONDS`
- `AI_RUNTIME_STRICT_SMOKE_TEST`

`AI_AGENT_RUNTIME_MODE` defaults to `Mock`. If `AI_API_KEY`, `AI_PROVIDER` or `AI_MODEL` is missing while Microsoft mode is requested, the runtime falls back to Mock and records the fallback without logging secrets.

`AI_TIMEOUT_SECONDS` controls the model request timeout. The default is 60 seconds; invalid values fall back to the default; values below 1 second or above 600 seconds are clamped. When `OpenAICompatibleModelClient` creates its own `HttpClient`, it sets `HttpClient.Timeout` from the runtime configuration. When an external `HttpClient` is injected, the caller's existing `Timeout` is not overwritten, but each request still receives a linked cancellation token derived from `AI_TIMEOUT_SECONDS`.

Provider failures are converted to structured runtime issues before returning to platform code:

- HTTP 401 / 403 -> `auth_error`
- HTTP 429 -> `rate_limit`
- HTTP 5xx and other non-success provider responses -> `provider_error`
- request timeout -> `timeout`
- invalid OpenAI-compatible JSON -> `invalid_provider_response`
- transport failures -> `network_error`

API keys are never written to code, appsettings, AuditLog messages, gateway responses, or runtime failure issues.

The real chief engineer runtime can understand the request and produce coordination advice. That advice is not authoritative execution. The wrapped chief engineer still runs the platform `ChiefEngineerOrchestrator`, which uses `SequentialWorkflowEngine`, Internal Agent workflow steps and QualityGate.

## Components

- `MicrosoftAgentAdapter`: wraps runtime-backed behavior as platform `IAgent`.
- `AgentFactory`: creates mock agents and can wrap only `chief-engineer` with Microsoft runtime.
- `MicrosoftRuntimeAgentInvoker`: invokes the runtime model and maps output to platform `AgentOutput`.
- `MicrosoftAgentOutputMapper`: converts JSON or text model output into platform `AgentOutput` and blocks permission escalation or direct Worker calls.
- `RuntimeConfiguration`: reads runtime mode and provider settings from environment variables.
- `IRuntimeModelClient`: internal provider abstraction for Microsoft Agent Framework and OpenAI-compatible fallback clients.
- `OpenAICompatibleModelClient`: performs OpenAI-compatible chat completion requests with configured timeout, cancellation support and structured provider errors.
- `MicrosoftWorkflowRuntime`: wraps sequential workflow execution using platform workflow contracts.
- `ToolBridge`: maps future runtime tool calls into platform `Skill` or `Worker` names and converts outputs into runtime messages.

## Boundaries

Agents must not directly call CAD Workers. CAD execution must continue to go through platform `WorkerContracts`, module boundaries, and `QualityGate`.

`ToolBridge` is only a mapping layer. It does not execute SolidWorks, AutoCAD, COM, SDK, API, or MCP calls.

Real LLM output must not:

- call Workers directly
- modify files
- change Agent visibility
- expose Internal Agents through Gateway
- skip WorkflowEngine
- skip QualityGate

Runtime failures also do not bypass QualityGate. If a real runtime call fails, the failure is returned as platform `AgentOutput` issues and the existing Gateway and workflow gate paths remain authoritative.
