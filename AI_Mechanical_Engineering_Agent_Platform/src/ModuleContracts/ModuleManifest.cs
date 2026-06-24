namespace ModuleContracts;

public sealed record ModuleManifest(
    string Name,
    string Version,
    string Description,
    IReadOnlyList<ModuleCapability> Capabilities,
    IReadOnlyList<ModuleDependency> Dependencies,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Workers,
    IReadOnlyList<string> Validators,
    IReadOnlyList<string> Reviewers);
