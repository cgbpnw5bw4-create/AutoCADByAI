namespace SkillContracts;

public sealed record SkillManifest(
    string Name,
    string Version,
    string Description,
    IReadOnlyList<string> InputSchemas,
    IReadOnlyList<string> OutputSchemas);
