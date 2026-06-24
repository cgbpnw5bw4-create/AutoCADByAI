namespace DomainSchemas;

public sealed record ErrorReport(
    string ErrorId,
    string Source,
    string Message,
    string Severity,
    IReadOnlyList<string> Diagnostics,
    DateTimeOffset CreatedAt);
