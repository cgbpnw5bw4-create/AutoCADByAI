namespace DomainSchemas;

public sealed record RejectReport(
    string RejectId,
    string SourceReviewId,
    IReadOnlyList<string> Reasons,
    string Message,
    DateTimeOffset CreatedAt);
