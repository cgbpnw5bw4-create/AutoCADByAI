namespace ModuleContracts;

public sealed record ModuleDependency(
    string Name,
    string VersionRange,
    bool Required);
