namespace ModuleContracts;

public enum ModuleExecutionStatus
{
    Completed,
    Rejected,
    Failed
}

public sealed record ModuleExecutionResult(
    ModuleExecutionStatus Status,
    string Message,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Logs);
