namespace DomainSchemas;

public sealed record MarkdownLanguageReport(
    int ScannedFiles,
    int PassedFiles,
    IReadOnlyList<string> FailedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> EnglishHeavySections,
    string FinalStatus);
