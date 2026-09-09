using DomainSchemas;
using PlatformCore;

namespace AgentGatewayHost;

public sealed record GatewayTaskResponse(PlatformTask Task, HumanApprovalRequest? PendingApproval,
    string? FailureStage, GatewayMessageResponse? Result);

public sealed record GatewayApprovalRequest(string WorkflowId, string ApprovalRequestId, string StepId,
    WorkflowApprovalDecision? Decision, string SubmittedBy, string? Comment = null);

public sealed record GatewayApprovalResponse(bool Accepted, GatewayTaskResponse? Task,
    string? FailureReason = null, bool NotFound = false);
