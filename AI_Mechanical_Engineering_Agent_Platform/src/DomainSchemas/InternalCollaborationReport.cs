namespace DomainSchemas;

public sealed record CalledAgentSummary(
    string AgentId,
    string AgentName,
    string Role,
    string Status,
    string Message,
    string? NextRecommendedAgentId);

public sealed record AgentOutputSnapshot(
    string AgentId,
    string Status,
    string Message,
    IReadOnlyList<ArtifactInfo> Artifacts,
    IReadOnlyList<string> Issues,
    string? NextRecommendedAgentId,
    ReviewReport? ReviewReport);

public sealed record InternalWorkflowStepSummary(
    string StepId,
    string StepName,
    string AgentId,
    string Status,
    GateDecision? GateDecision,
    int RetryCount,
    int MaxRetries,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Logs,
    HumanApprovalResolution? ApprovalResolution = null);

public sealed record RetrySummary(
    int TotalRetries,
    IReadOnlyList<string> RetriedStepIds);

public sealed record InternalCollaborationReport(
    string ConversationId,
    string RootAgentId,
    IReadOnlyList<CalledAgentSummary> CalledAgents,
    IReadOnlyList<AgentOutputSnapshot> AgentOutputs,
    IReadOnlyList<string> Issues,
    IReadOnlyList<ArtifactInfo> Artifacts,
    string Summary,
    string FinalRecommendation,
    string? WorkflowId = null,
    string WorkflowStatus = "NotStarted",
    IReadOnlyList<InternalWorkflowStepSummary>? StepResults = null,
    GateDecision? FinalGateDecision = null,
    RetrySummary? RetrySummary = null,
    FailureReport? FailureReport = null,
    HumanApprovalRequest? HumanApprovalRequest = null);
