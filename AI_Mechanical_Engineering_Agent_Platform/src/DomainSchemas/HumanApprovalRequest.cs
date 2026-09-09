namespace DomainSchemas;

public sealed record HumanApprovalRequest(
    string WorkflowId,
    string StepId,
    string StepName,
    string RequestedBy,
    string Reason,
    IReadOnlyList<string> Options,
    string ContextSummary,
    DateTimeOffset CreatedAt,
    string? ApprovalRequestId = null);
