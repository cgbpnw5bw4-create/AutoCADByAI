namespace DomainSchemas;

public sealed record ArtifactInfo(
    string Id,
    string Name,
    string Kind,
    string Path,
    string? MediaType = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
