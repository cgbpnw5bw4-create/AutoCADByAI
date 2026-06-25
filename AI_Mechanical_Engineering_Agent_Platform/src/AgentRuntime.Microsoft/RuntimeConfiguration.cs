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
        var timeoutSeconds = int.TryParse(Get(environment, "AI_TIMEOUT_SECONDS"), out var parsedTimeout)
            ? parsedTimeout
            : 30;
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
            Math.Max(1, timeoutSeconds),
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

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
