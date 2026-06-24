namespace DomainSchemas;

public sealed record FinalReport(
    string TaskId,
    string Status,
    string Summary,
    IReadOnlyList<ArtifactInfo> Artifacts,
    IReadOnlyList<ReviewReport> Reviews,
    IReadOnlyList<ErrorReport> Errors,
    DateTimeOffset CreatedAt);
