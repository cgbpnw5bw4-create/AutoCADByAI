using DomainSchemas;

namespace QualityGate;

public class DefaultRetryPolicy : IRetryPolicy
{
    private static readonly string[] DefaultNonRetryableMarkers = ["critical", "non_retryable"];

    public DefaultRetryPolicy(
        int maxRetries = 2,
        IReadOnlyCollection<string>? retryableIssueTypes = null,
        IReadOnlyCollection<string>? nonRetryableIssueTypes = null)
    {
        MaxRetries = maxRetries;
        RetryableIssueTypes = retryableIssueTypes?.ToArray() ?? Array.Empty<string>();
        NonRetryableIssueTypes = nonRetryableIssueTypes?.ToArray() ?? DefaultNonRetryableMarkers;
    }

    public int MaxRetries { get; }

    public IReadOnlyCollection<string> RetryableIssueTypes { get; }

    public IReadOnlyCollection<string> NonRetryableIssueTypes { get; }

    public virtual TimeSpan GetDelay(int retryCount) => TimeSpan.Zero;

    public virtual bool ShouldRetry(GateDecision decision, int retryCount, IEnumerable<Issue> issues)
    {
        var issueArray = issues.ToArray();
        if (decision.Result != GateDecisionResult.Rejected)
        {
            return false;
        }

        if (retryCount >= MaxRetries)
        {
            return false;
        }

        if (ContainsNonRetryableIssue(issueArray))
        {
            return false;
        }

        return RetryableIssueTypes.Count == 0 || ContainsRetryableIssue(issueArray);
    }

    public bool ShouldRetry(GateDecision decision, int retryCount, IReadOnlyList<string> issues) =>
        ShouldRetry(decision, retryCount, issues.Select(Issue.FromText));

    private bool ContainsRetryableIssue(IReadOnlyList<Issue> issues) =>
        issues.Any(issue => RetryableIssueTypes.Any(marker =>
            issue.Type.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
            issue.Message.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private bool ContainsNonRetryableIssue(IReadOnlyList<Issue> issues) =>
        issues.Any(issue =>
            string.Equals(issue.Severity, "critical", StringComparison.OrdinalIgnoreCase) ||
            NonRetryableIssueTypes.Any(marker =>
                issue.Type.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                issue.Message.Contains(marker, StringComparison.OrdinalIgnoreCase)));
}
