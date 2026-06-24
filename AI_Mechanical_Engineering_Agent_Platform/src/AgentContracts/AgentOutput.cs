using DomainSchemas;

namespace AgentContracts;

public enum AgentOutputStatus
{
    Completed,
    WaitingForReview,
    Rejected,
    Failed,
    NeedsHumanApproval
}

public sealed record AgentOutput(
    AgentOutputStatus Status,
    string Message,
    IReadOnlyList<ArtifactInfo> Artifacts,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Logs,
    string? NextRecommendedAgentId);
