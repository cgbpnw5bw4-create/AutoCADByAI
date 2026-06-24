# Platform Blocker Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix platform skeleton blockers before the next AI mechanical engineering agent phase.

**Architecture:** Keep the work at platform boundary level only. Load Module manifests from `module.yaml`, keep fallback manifests for resilience, route Gateway messages through QualityGate, and add storage contracts without connecting real CAD, OpenClaw, LLMs, or databases.

**Tech Stack:** C#/.NET 10, ASP.NET Core minimal API, xUnit.

---

### Task 1: Solution And Module Structure

**Files:**
- Create: `AI_Mechanical_Engineering_Agent_Platform.sln`
- Create: `src/Modules/*/{agents,skills,workers,validators,reviewers,schemas,tests}/.gitkeep`
- Test: `tests/PlatformSelfCheck.Tests/PlatformBlockerTests.cs`

- [x] Create `.sln` solution file.
- [x] Add all existing `.csproj` files to the solution.
- [x] Add `.gitkeep` files to all standard module subdirectories.
- [x] Test that every Module has the standard structure.

### Task 2: Module Manifest Loading

**Files:**
- Create: `src/PlatformCore/ModuleRegistry/ModuleManifestLoader.cs`
- Modify: `src/PlatformCore/ModuleRegistry/ModuleRegistry.cs`
- Modify: `src/PlatformCore/PlatformBootstrapper.cs`

- [x] Load `module.yaml` files from `src/Modules`.
- [x] Register module source as `yaml` or `fallback`.
- [x] Record YAML load errors through EventBus and AuditLog.
- [x] Keep fallback manifests for missing or failed YAML manifests.

### Task 3: Gateway QualityGate

**Files:**
- Modify: `src/Interfaces/AgentGatewayHost/AgentMessageDispatcher.cs`
- Modify: `src/Interfaces/AgentGatewayHost/GatewayMessageResponse.cs`
- Create: `src/PlatformCore/AgentOutputReviewMapper.cs`

- [x] Keep Gateway limited to Public agents.
- [x] Convert AgentOutput to ReviewReport.
- [x] Evaluate with DefaultGatekeeper.
- [x] Return GateDecision and RejectReport when rejected.

### Task 4: Storage And Self-Check

**Files:**
- Create: `src/Storage/*.cs`
- Modify: `src/PlatformCore/SelfCheckReport.cs`
- Modify: `src/PlatformCore/PlatformSelfCheckRunner.cs`

- [x] Add storage abstraction interfaces.
- [x] Add self-check fields for solution, modules, manifest loading, QualityGate, Storage and RejectReport.
- [x] Fail self-check if any P0 platform check fails.
- [x] Verify `.sln` build, xUnit tests and CLI self-check.
