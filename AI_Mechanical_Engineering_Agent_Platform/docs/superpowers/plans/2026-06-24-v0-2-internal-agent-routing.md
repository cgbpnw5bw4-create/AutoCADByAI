# V0.2 Internal Agent Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the public chief-engineer Agent internally route work through Internal Agents while Gateway still exposes only chief-engineer.

**Architecture:** Keep Gateway as the external boundary. Add `InternalAgentRouter` for platform-internal calls, `ChiefEngineerOrchestrator` for the fixed V0.2 collaboration route, and `InternalCollaborationReport` as the structured handoff into QualityGate.

**Tech Stack:** C#/.NET 10, ASP.NET Core minimal API, xUnit.

---

### Task 1: Red Tests For Internal Routing

**Files:**
- Create: `tests/PlatformSelfCheck.Tests/InternalAgentRoutingTests.cs`

- [x] Test InternalAgentRouter sequentially invokes internal agents.
- [x] Test chief-engineer creates InternalCollaborationReport.
- [x] Test Gateway response includes collaboration report and QualityGate result.
- [x] Test Gateway still blocks direct internal agent calls.
- [x] Test self-check report includes V0.2 routing fields.

### Task 2: Collaboration Schema And Router

**Files:**
- Create: `src/DomainSchemas/InternalCollaborationReport.cs`
- Create: `src/PlatformCore/AgentRegistry/InternalAgentRouter.cs`
- Modify: `src/AgentContracts/AgentOutput.cs`

- [x] Add called-agent and agent-output snapshots.
- [x] Add optional collaboration report and review report to AgentOutput.
- [x] Add audited internal agent invocation.

### Task 3: Chief Engineer Orchestration

**Files:**
- Create: `src/Modules/RequirementUnderstanding/agents/ChiefEngineerOrchestrator.cs`
- Modify: `src/PlatformCore/AgentRegistry/PlaceholderAgent.cs`
- Modify: `src/PlatformCore/PlatformBootstrapper.cs`
- Modify: `src/PlatformCore/PlatformCore.csproj`

- [x] Route chief-engineer through mechanical-designer, cad-modeler, drawing-engineer and drawing-reviewer.
- [x] Keep Internal Agents simulated and structured.
- [x] Avoid Worker calls and real CAD integration.

### Task 4: Gateway, QualityGate And Self-Check

**Files:**
- Modify: `src/Interfaces/AgentGatewayHost/AgentMessageDispatcher.cs`
- Modify: `src/Interfaces/AgentGatewayHost/GatewayMessageResponse.cs`
- Modify: `src/PlatformCore/AgentOutputReviewMapper.cs`
- Modify: `src/PlatformCore/PlatformSelfCheckRunner.cs`
- Modify: `src/PlatformCore/SelfCheckReport.cs`

- [x] Return collaboration report from Gateway chief-engineer calls.
- [x] Evaluate collaboration output through QualityGate.
- [x] Add required AuditLog actions.
- [x] Add self-check V0.2 fields.
- [x] Verify build, tests, self-check and live Gateway behavior.
