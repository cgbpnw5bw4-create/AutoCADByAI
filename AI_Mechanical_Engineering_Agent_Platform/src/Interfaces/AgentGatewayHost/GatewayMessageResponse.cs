using DomainSchemas;

namespace AgentGatewayHost;

public sealed record GatewayMessageResponse(
    string AgentId,
    string AgentName,
    string Status,
    string Message,
    IReadOnlyList<ArtifactInfo> Artifacts,
    IReadOnlyList<string> Issues,
    GateDecision GateDecision,
    RejectReport? RejectReport,
    string? NextRecommendedAgent);
