namespace WorkerContracts;

public sealed record WorkerManifest(
    string Name,
    string Version,
    string TargetSystem,
    string Description,
    IReadOnlyList<string> Capabilities);
