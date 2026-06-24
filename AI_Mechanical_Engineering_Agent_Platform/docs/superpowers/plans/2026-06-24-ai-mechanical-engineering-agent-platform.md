# AI Mechanical Engineering Agent Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a runnable C#/.NET platform skeleton for mechanical engineering multi-agent orchestration.

**Architecture:** Keep platform contracts independent from agent runtime and CAD execution. Expose only Public agents through AgentGatewayHost. Use fake workers and self-check to verify the skeleton.

**Tech Stack:** C#/.NET 10, ASP.NET Core minimal API, xUnit.

---

### Task 1: Create Contracts And Schemas

**Files:**
- Create: `src/AgentContracts/*.cs`
- Create: `src/ModuleContracts/*.cs`
- Create: `src/SkillContracts/*.cs`
- Create: `src/WorkerContracts/*.cs`
- Create: `src/DomainSchemas/*.cs`
- Test: `tests/PlatformSelfCheck.Tests/PlatformBootstrapperTests.cs`

- [x] Define structured Agent, Module, Skill and Worker contracts.
- [x] Define CADModelSpec, BuildSpec, DrawingSpec, ReviewReport, RejectReport, GateDecision, ArtifactInfo, ErrorReport and FinalReport.
- [x] Verify tests fail before platform implementation.

### Task 2: Implement Platform Core

**Files:**
- Create: `src/PlatformCore/TaskSystem/*.cs`
- Create: `src/PlatformCore/WorkflowEngine/*.cs`
- Create: `src/PlatformCore/*Registry/*.cs`
- Create: `src/PlatformCore/EventBus/*.cs`
- Create: `src/PlatformCore/AuditLog/*.cs`
- Create: `src/PlatformCore/PlatformBootstrapper.cs`
- Create: `src/PlatformCore/PlatformSelfCheckRunner.cs`

- [x] Implement task lifecycle and registries.
- [x] Register the six built-in placeholder agents.
- [x] Ensure only `chief-engineer` is Public.
- [x] Generate self-check report data.

### Task 3: Implement Gateway, QualityGate And Workers

**Files:**
- Create: `src/Interfaces/AgentGatewayHost/*.cs`
- Create: `src/QualityGate/**/*.cs`
- Create: `src/Workers/SolidWorks/*.cs`
- Create: `src/Workers/AutoCAD/*.cs`

- [x] Implement `GET /agents`.
- [x] Implement `POST /agents/{agentId}/message`.
- [x] Implement fake SolidWorks and AutoCAD workers.
- [x] Implement GateDecisionPolicy and RejectReportBuilder.

### Task 4: Implement CLI And Documentation

**Files:**
- Modify: `src/Interfaces/CliHost/Program.cs`
- Create: `src/Modules/**/README.md`
- Create: `src/Modules/**/module.yaml`
- Create: `docs/*.md`

- [x] Add `self-check` command.
- [x] Write architecture and standards documentation.
- [x] Run `dotnet test`.
- [x] Run `dotnet run --project src/Interfaces/CliHost -- self-check`.
