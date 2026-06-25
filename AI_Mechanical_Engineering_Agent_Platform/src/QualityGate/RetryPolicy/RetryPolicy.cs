using DomainSchemas;

namespace QualityGate;

public enum BackoffStrategy
{
    None
}

public sealed class RetryPolicy
{
    private static readonly string[] DefaultNonRetryableMarkers = ["critical", "non_retryable"];

    public RetryPolicy(
        int maxRetries = 2,
        IReadOnlyCollection<string>? retryableIssueTypes = null,
        IReadOnlyCollection<string>? nonRetryableIssueTypes = null,
        BackoffStrategy backoffStrategy = BackoffStrategy.None)
    {
        MaxRetries = maxRetries;
        RetryableIssueTypes = retryableIssueTypes?.ToArray() ?? Array.Empty<string>();
        NonRetryableIssueTypes = nonRetryableIssueTypes?.ToArray() ?? DefaultNonRetryableMarkers;
        BackoffStrategy = backoffStrategy;
        Delay = TimeSpan.Zero;
        RouteToErrorDiagnosisOnFinalFailure = true;
    }

    public RetryPolicy(int MaxAttempts, TimeSpan Delay, bool RouteToErrorDiagnosisOnFinalFailure)
        : this(Math.Max(0, MaxAttempts - 1))
    {
        this.Delay = Delay;
        this.RouteToErrorDiagnosisOnFinalFailure = RouteToErrorDiagnosisOnFinalFailure;
    }

    public int MaxRetries { get; }

    public int MaxAttempts => MaxRetries + 1;

    public IReadOnlyCollection<string> RetryableIssueTypes { get; }

    public IReadOnlyCollection<string> NonRetryableIssueTypes { get; }

    public BackoffStrategy BackoffStrategy { get; }

    public TimeSpan Delay { get; }

    public bool RouteToErrorDiagnosisOnFinalFailure { get; }

    public bool ShouldRetry(GateDecision decision, int retryCount, IReadOnlyList<string> issues)
    {
        if (decision.Result != GateDecisionResult.Rejected)
        {
            return false;
        }

        if (retryCount >= MaxRetries)
        {
            return false;
        }

        if (ContainsNonRetryableIssue(issues))
        {
            return false;
        }

        return RetryableIssueTypes.Count == 0 || ContainsRetryableIssue(issues);
    }

    private bool ContainsRetryableIssue(IReadOnlyList<string> issues) =>
        issues.Any(issue => RetryableIssueTypes.Any(marker =>
            issue.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private bool ContainsNonRetryableIssue(IReadOnlyList<string> issues) =>
        issues.Any(issue => NonRetryableIssueTypes.Any(marker =>
            issue.Contains(marker, StringComparison.OrdinalIgnoreCase)));
}
