# Architecture

AI_Mechanical_Engineering_Agent_Platform is organized as a long-term hostable multi-agent platform for mechanical engineering automation.

## PlatformCore

Provides platform services that should stay independent from any specific agent runtime or CAD tool:

- TaskSystem: task lifecycle and status
- WorkflowEngine: sequential workflow execution and step result tracking
- AgentRegistry, SkillRegistry, ModuleRegistry, WorkerRegistry: in-memory registration and lookup
- ModuleManifestLoader: loads `module.yaml` files and records fallback usage
- InternalAgentRouter: invokes Internal Agents through AgentRegistry and records audit logs
- ContextManager: workflow context creation
- PermissionManager: visibility and gateway exposure checks
- EventBus: in-memory system events
- AuditLog: task, agent, skill, worker and gate audit entries

## Contracts

`AgentContracts`, `ModuleContracts`, `SkillContracts` and `WorkerContracts` define stable platform boundaries.

Agents decide and coordinate. Skills transform and assist. Workers execute external systems. Modules package complete capability areas rather than loose scripts.

## DomainSchemas

Contains shared structured data for mechanical and CAD work:

- CADModelSpec
- BuildSpec
- DrawingSpec
- ReviewReport
- RejectReport
- GateDecision
- ArtifactInfo
- ErrorReport
- FinalReport
- InternalCollaborationReport

Core task state must move through these schemas, not only through natural-language messages.

V0.2 internal routing uses `InternalCollaborationReport` to preserve called agents, output snapshots, issues, artifacts, summary and recommendation.

## AgentRuntime.Microsoft

This is the only project intended to reference Microsoft Agent Framework packages. It adapts Microsoft runtime concepts to the platform contracts.

Current version is a placeholder adapter layer. Business modules, workers and contracts do not depend on Microsoft runtime APIs.

## Modules

Each module is a complete capability board containing agents, skills, workers, validators, reviewers, schemas and tests.

Current modules:

- RequirementUnderstanding
- MechanicalDesign
- CADModeling
- DrawingGeneration
- DrawingReview
- CodeEngineering
- CodeReview
- ErrorDiagnosis

## Workers

Workers are the future execution layer for SolidWorks, AutoCAD and other industrial software APIs, SDKs, COM servers or MCP bridges.

The first version includes fake SolidWorks and AutoCAD workers only.

## QualityGate

QualityGate owns validation, review, gate decisions, reject reports and retry policy. It consumes `ReviewReport` and returns `GateDecision`.

AgentGatewayHost routes every external Agent message through the minimal QualityGate chain before returning a response.

## Storage

Storage contains persistence contracts only:

- ITaskRepository
- IAuditLogRepository
- IEventStore
- IArtifactRepository
- IReportRepository

No database implementation is attached in the platform skeleton.

## Interfaces

Interfaces host external entry points:

- CliHost: local self-check and maintenance commands
- ApiHost: reserved for future platform APIs
- AgentGatewayHost: HTTP gateway for OpenClaw, Feishu, WeCom, Slack or web frontends

External systems must use AgentGatewayHost instead of reading project folders or calling workers directly.
