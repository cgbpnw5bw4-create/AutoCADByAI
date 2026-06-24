# V0.3 Microsoft Runtime Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the minimal Microsoft Agent Framework adapter boundary without letting Microsoft runtime APIs become platform contracts.

**Architecture:** Keep `AgentRuntime.Microsoft` as the only project with the Microsoft Agent Framework package reference. Use MockRuntime by default for self-check and local execution. Keep PlatformCore, contracts, schemas, QualityGate and Workers free of Microsoft Agent Framework compile-time dependencies.

**Tech Stack:** C#/.NET 10, Microsoft.Agents.AI 1.11.0, xUnit.

---

### Task 1: Runtime Adapter Tests

**Files:**
- Create: `tests/PlatformSelfCheck.Tests/AgentRuntimeMicrosoftTests.cs`
- Modify: `tests/PlatformSelfCheck.Tests/PlatformSelfCheck.Tests.csproj`

- [x] Test package reference isolation.
- [x] Test contracts do not expose Microsoft Agent Framework types.
- [x] Test MicrosoftAgentAdapter implements `IAgent`.
- [x] Test MockRuntime creates six platform agents.
- [x] Test mock sequential workflow.
- [x] Test self-check V0.3 fields.

### Task 2: Runtime Types

**Files:**
- Create: `src/AgentRuntime.Microsoft/AgentRuntimeMode.cs`
- Create: `src/AgentRuntime.Microsoft/RuntimeAgentManifest.cs`
- Create: `src/AgentRuntime.Microsoft/MockAgentRuntime.cs`
- Create: `src/AgentRuntime.Microsoft/IMicrosoftRuntimeAgentInvoker.cs`
- Modify: `src/AgentRuntime.Microsoft/MicrosoftAgentAdapter.cs`
- Modify: `src/AgentRuntime.Microsoft/AgentFactory.cs`

- [x] Add `Mock` and `Microsoft` runtime modes.
- [x] Keep Microsoft mode behind an invoker interface that returns platform `AgentOutput`.
- [x] Return deterministic mock `AgentOutput` without model or CAD calls.

### Task 3: Workflow And Tools

**Files:**
- Modify: `src/AgentRuntime.Microsoft/MicrosoftWorkflowRuntime.cs`
- Modify: `src/AgentRuntime.Microsoft/ToolBridge.cs`

- [x] Run platform sequential workflow steps in MockRuntime.
- [x] Add tool-call mapping skeletons for Skill and Worker names.
- [x] Convert Skill/Worker outputs into runtime messages without executing CAD tools in V0.3.

### Task 4: Platform Switch And Self-Check

**Files:**
- Modify: `src/PlatformCore/PlatformBootstrapper.cs`
- Modify: `src/PlatformCore/SelfCheckReport.cs`
- Modify: `src/PlatformCore/PlatformSelfCheckRunner.cs`
- Modify: `src/AgentRuntime.Microsoft/README.md`

- [x] Add optional runtime agent factory registration to PlatformBootstrapper.
- [x] Use reflection in self-check to avoid PlatformCore referencing AgentRuntime.Microsoft.
- [x] Verify runtime package isolation and MockRuntime behavior.
- [x] Document boundaries and future MicrosoftRuntime connection points.
