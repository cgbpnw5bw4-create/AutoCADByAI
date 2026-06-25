namespace AgentRuntime.Microsoft;

public sealed record RuntimeConfiguration(
    AgentRuntimeMode RequestedMode,
    AgentRuntimeMode EffectiveMode,
    string? Provider,
    string? Model,
    string? ApiKey,
    string? BaseUrl,
    double? Temperature,
    int TimeoutSeconds,
    bool FallbackUsed,
    string? FallbackReason,
    bool StrictSmokeTest)
{
    public const int DefaultTimeoutSeconds = 60;

    public const int MinTimeoutSeconds = 1;

    public const int MaxTimeoutSeconds = 600;

    public static RuntimeConfiguration FromEnvironment() =>
        FromEnvironment(Environment.GetEnvironmentVariables()
            .Keys
            .OfType<string>()
            .ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.OrdinalIgnoreCase));

    public static RuntimeConfiguration FromEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        var requestedMode = ParseMode(Get(environment, "AI_AGENT_RUNTIME_MODE"));
        var provider = EmptyToNull(Get(environment, "AI_PROVIDER"));
        var model = EmptyToNull(Get(environment, "AI_MODEL"));
        var apiKey = EmptyToNull(Get(environment, "AI_API_KEY"));
        var baseUrl = EmptyToNull(Get(environment, "AI_BASE_URL"));
        double? temperature = double.TryParse(Get(environment, "AI_TEMPERATURE"), out var parsedTemperature)
            ? parsedTemperature
            : null;
        var timeoutSeconds = ParseTimeoutSeconds(Get(environment, "AI_TIMEOUT_SECONDS"));
        var strictSmokeTest = string.Equals(Get(environment, "AI_RUNTIME_STRICT_SMOKE_TEST"), "true", StringComparison.OrdinalIgnoreCase);

        var missingRuntimeConfiguration =
            requestedMode == AgentRuntimeMode.Microsoft &&
            (provider is null || model is null || apiKey is null);
        var effectiveMode = missingRuntimeConfiguration ? AgentRuntimeMode.Mock : requestedMode;

        return new RuntimeConfiguration(
            requestedMode,
            effectiveMode,
            provider,
            model,
            apiKey,
            baseUrl,
            temperature,
            timeoutSeconds,
            missingRuntimeConfiguration,
            missingRuntimeConfiguration ? "Missing required runtime credential or provider/model environment variable." : null,
            strictSmokeTest);
    }

    private static string? Get(IReadOnlyDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) ? value : null;

    private static AgentRuntimeMode ParseMode(string? value) =>
        Enum.TryParse<AgentRuntimeMode>(value, ignoreCase: true, out var mode)
            ? mode
            : AgentRuntimeMode.Mock;

    public static int NormalizeTimeoutSeconds(int timeoutSeconds) =>
        Math.Clamp(timeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);

    private static int ParseTimeoutSeconds(string? value) =>
        int.TryParse(value, out var parsedTimeout)
            ? NormalizeTimeoutSeconds(parsedTimeout)
            : DefaultTimeoutSeconds;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
