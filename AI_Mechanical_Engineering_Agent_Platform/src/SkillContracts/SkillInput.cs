namespace SkillContracts;

public sealed record SkillInput(
    string TaskId,
    string PayloadType,
    object Payload,
    IReadOnlyDictionary<string, string> Context);
