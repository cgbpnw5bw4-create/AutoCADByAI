namespace DomainSchemas;

public sealed record BuildOperation(
    string Operation,
    string Target,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record BuildSpec(
    string Id,
    string CADModelSpecId,
    IReadOnlyList<BuildOperation> Steps,
    string TargetWorker,
    string ExpectedOutputKind);
