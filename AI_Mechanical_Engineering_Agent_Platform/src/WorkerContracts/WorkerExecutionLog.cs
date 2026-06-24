namespace WorkerContracts;

public sealed record WorkerExecutionLog(
    string WorkerName,
    string TargetSystem,
    string TaskId,
    string Status,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
