using DomainSchemas;

namespace QualityGate;

public interface IRetryPolicy
{
    int MaxRetries { get; }

    bool ShouldRetry(GateDecision decision, int retryCount, IEnumerable<Issue> issues);

    TimeSpan GetDelay(int retryCount);
}
