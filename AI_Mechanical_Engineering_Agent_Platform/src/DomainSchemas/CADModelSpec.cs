namespace DomainSchemas;

public sealed record CADModelSpec(
    string Id,
    string ModelType,
    string Title,
    string Description,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> Materials,
    IReadOnlyList<string> Constraints);
