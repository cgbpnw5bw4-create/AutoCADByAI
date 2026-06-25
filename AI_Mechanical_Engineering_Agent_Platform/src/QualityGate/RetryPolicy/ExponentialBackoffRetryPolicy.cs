namespace QualityGate;

public sealed class ExponentialBackoffRetryPolicy : DefaultRetryPolicy
{
    public ExponentialBackoffRetryPolicy(
        int maxRetries = 2,
        int baseDelayMs = 100,
        int maxDelayMs = 5_000,
        double multiplier = 2.0)
        : base(maxRetries)
    {
        BaseDelayMs = baseDelayMs;
        MaxDelayMs = maxDelayMs;
        Multiplier = multiplier;
    }

    public int BaseDelayMs { get; }

    public int MaxDelayMs { get; }

    public double Multiplier { get; }

    public override TimeSpan GetDelay(int retryCount)
    {
        var delay = BaseDelayMs * Math.Pow(Multiplier, Math.Max(0, retryCount));
        return TimeSpan.FromMilliseconds(Math.Min(MaxDelayMs, delay));
    }
}
