namespace DomainSchemas;

public sealed record FailureReport(
    string WorkflowId,
    string FailedStepId,
    string FailedStepName,
    string FailedAgentId,
    string FailureReason,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Logs,
    string RecommendedAction);
