namespace AgentContracts;

public sealed record AgentExecutionLog(
    string AgentId,
    string TaskId,
    string Status,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
