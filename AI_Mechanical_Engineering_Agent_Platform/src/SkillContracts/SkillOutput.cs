namespace SkillContracts;

public enum SkillOutputStatus
{
    Completed,
    Rejected,
    Failed
}

public sealed record SkillOutput(
    SkillOutputStatus Status,
    object? Result,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Logs);
