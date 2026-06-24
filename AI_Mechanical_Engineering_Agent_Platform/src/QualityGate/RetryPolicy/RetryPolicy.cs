namespace QualityGate;

public sealed record RetryPolicy(
    int MaxAttempts,
    TimeSpan Delay,
    bool RouteToErrorDiagnosisOnFinalFailure);
