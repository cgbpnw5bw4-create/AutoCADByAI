namespace DomainSchemas;

public sealed record DrawingSpec(
    string Id,
    string SourceModelArtifactId,
    string SheetSize,
    string ProjectionStandard,
    IReadOnlyList<string> RequiredViews,
    IReadOnlyList<string> RequiredDimensions,
    IReadOnlyDictionary<string, string> TitleBlockFields);
