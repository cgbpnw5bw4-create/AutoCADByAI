using DomainSchemas;
using PlatformCore;

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
    InternalCollaborationReport? CollaborationReport,
    string? NextRecommendedAgent,
    string RuntimeMode = "Mock",
    string? RuntimeProvider = null,
    string? RuntimeModel = null,
    bool RuntimeFallbackUsed = false,
    string? RuntimeFallbackReason = null,
    bool ChiefEngineerRuntimeUsed = false,
    string? TaskId = null,
    PlatformTaskStatus? TaskStatus = null,
    HumanApprovalRequest? PendingApproval = null,
    string? TaskAccessToken = null,
    string? FailureStage = null);
