using DomainSchemas;

namespace PlatformCore;

public sealed record ModuleSummary(string Name, string Version, string Description);

public sealed record AgentSummary(string Id, string Name, string Role, string Visibility);

public sealed record SkillSummary(string Name, string Description);

public sealed record WorkerSummary(string Name, string TargetSystem);

public sealed record ModuleStructureCheck(
    string ModuleName,
    bool Passed,
    IReadOnlyList<string> MissingEntries);

public sealed record PlatformSelfCheckReport(
    IReadOnlyList<ModuleSummary> RegisteredModules,
    IReadOnlyList<AgentSummary> RegisteredAgents,
    IReadOnlyList<AgentSummary> PublicAgents,
    IReadOnlyList<AgentSummary> InternalAgents,
    IReadOnlyList<SkillSummary> RegisteredSkills,
    IReadOnlyList<WorkerSummary> RegisteredWorkers,
    IReadOnlyList<WorkflowStepResult> WorkflowSteps,
    GateDecision GateDecision,
    IReadOnlyList<AgentDirectoryEntry> GatewayVisibleAgents,
    IReadOnlyList<AuditLogEntry> AuditLogs,
    bool SolutionExists,
    IReadOnlyList<ModuleStructureCheck> ModuleStructureChecks,
    string ModuleManifestSource,
    IReadOnlyList<string> YamlLoadedModules,
    IReadOnlyList<string> FallbackModules,
    bool GatewayQualityGateEnabled,
    bool StorageContractsRegistered,
    bool RejectReportBuilderCheck,
    bool InternalRoutingEnabled,
    IReadOnlyList<string> InternalAgentsInvoked,
    bool CollaborationReportCreated,
    bool GatewayBlocksInternalAgents,
    bool QualityGateAfterCollaboration,
    bool AuditInternalAgentCalls,
    bool AgentRuntimeProjectExists,
    bool MicrosoftRuntimeDependencyIsolated,
    string RuntimeMode,
    bool MockRuntimeAgentCreation,
    bool MicrosoftAgentAdapterCheck,
    bool MicrosoftWorkflowRuntimeCheck,
    bool RuntimeTypesDoNotLeakToContracts,
    bool GatewayVisibilityStillValid,
    bool QualityGateStillEnabled,
    string FinalStatus);
