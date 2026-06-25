namespace DomainSchemas;

public sealed record Issue(
    string Type,
    string Severity,
    string Message)
{
    public static Issue FromText(string text)
    {
        var severity =
            Contains(text, "critical") || Contains(text, "fatal") ? "critical" :
            Contains(text, "warning") ? "warning" :
            "info";
        var type =
            Contains(text, "non_retryable") ? "non_retryable" :
            Contains(text, "needs_human_approval") ? "needs_human_approval" :
            Contains(text, "retryable") ? "retryable" :
            severity == "critical" ? "critical" :
            "general";

        return new Issue(type, severity, text);
    }

    private static bool Contains(string text, string marker) =>
        text.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
