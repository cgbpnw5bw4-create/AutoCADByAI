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

public sealed record InternalCollaborationReport(
    string ConversationId,
    string RootAgentId,
    IReadOnlyList<CalledAgentSummary> CalledAgents,
    IReadOnlyList<AgentOutputSnapshot> AgentOutputs,
    IReadOnlyList<string> Issues,
    IReadOnlyList<ArtifactInfo> Artifacts,
    string Summary,
    string FinalRecommendation);
