namespace ModuleContracts;

public sealed record ModuleCapability(
    string Name,
    string Description,
    IReadOnlyList<string> InputSchemas,
    IReadOnlyList<string> OutputSchemas);
