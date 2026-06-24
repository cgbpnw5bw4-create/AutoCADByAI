namespace DomainSchemas;

public enum GateDecisionResult
{
    Passed,
    Rejected,
    Failed,
    NeedsHumanApproval
}

public sealed record GateDecision(
    string Id,
    GateDecisionResult Result,
    string Reason,
    string? NextAction = null,
    RejectReport? RejectReport = null);
