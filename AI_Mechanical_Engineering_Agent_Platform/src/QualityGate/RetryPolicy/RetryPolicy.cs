namespace QualityGate;

public enum BackoffStrategy
{
    None
}

public sealed class RetryPolicy : DefaultRetryPolicy
{
    public RetryPolicy(
        int maxRetries = 2,
        IReadOnlyCollection<string>? retryableIssueTypes = null,
        IReadOnlyCollection<string>? nonRetryableIssueTypes = null,
        BackoffStrategy backoffStrategy = BackoffStrategy.None)
        : base(maxRetries, retryableIssueTypes, nonRetryableIssueTypes)
    {
        BackoffStrategy = backoffStrategy;
        Delay = TimeSpan.Zero;
        RouteToErrorDiagnosisOnFinalFailure = true;
    }

    public RetryPolicy(int MaxAttempts, TimeSpan Delay, bool RouteToErrorDiagnosisOnFinalFailure)
        : base(Math.Max(0, MaxAttempts - 1))
    {
        BackoffStrategy = BackoffStrategy.None;
        this.Delay = Delay;
        this.RouteToErrorDiagnosisOnFinalFailure = RouteToErrorDiagnosisOnFinalFailure;
    }

    public int MaxAttempts => MaxRetries + 1;

    public BackoffStrategy BackoffStrategy { get; }

    public TimeSpan Delay { get; }

    public bool RouteToErrorDiagnosisOnFinalFailure { get; }
}
