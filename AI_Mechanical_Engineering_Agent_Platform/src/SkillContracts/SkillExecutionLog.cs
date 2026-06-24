namespace SkillContracts;

public sealed record SkillExecutionLog(
    string SkillName,
    string TaskId,
    string Status,
    IReadOnlyList<string> Logs,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
