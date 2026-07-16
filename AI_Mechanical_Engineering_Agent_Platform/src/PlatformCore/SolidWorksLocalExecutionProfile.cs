using System.Text.Json;

namespace PlatformCore;

/// <summary>
/// Local-only authorization for the V1.7 real SolidWorks workflow. This profile is intentionally
/// loaded only by the CLI/E2E boundary; it is never consulted by CI or self-check execution.
/// </summary>
public sealed record SolidWorksLocalExecutionProfile(
    bool RealExecutionAuthorized,
    string ExecutionAuthorizationSource,
    bool Visible,
    string? TemplatePartPath,
    string? TemplateDrawingPath,
    int? ConnectTimeoutSeconds,
    int? ExecutionTimeoutSeconds,
    string ConfigurationPath,
    IReadOnlyList<string> Issues)
{
    public const string ConfigurationRelativePath = "config/solidworks.local.json";
    public const string AuthorizationSource = "LocalDevelopmentProfile";

    public bool IsAuthorized =>
        RealExecutionAuthorized &&
        string.Equals(ExecutionAuthorizationSource, AuthorizationSource, StringComparison.Ordinal);

    public static SolidWorksLocalExecutionProfile Load(string projectRoot)
    {
        var configurationPath = Path.GetFullPath(Path.Combine(projectRoot, ConfigurationRelativePath));
        if (!File.Exists(configurationPath))
        {
            return Disabled(configurationPath, "local_development_profile_missing: config/solidworks.local.json was not found.");
        }

        try
        {
            var configuration = JsonSerializer.Deserialize<LocalConfiguration>(
                File.ReadAllText(configurationPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            if (configuration is null)
            {
                return Disabled(configurationPath, "local_development_profile_invalid: the local authorization file was empty.");
            }

            var source = string.IsNullOrWhiteSpace(configuration.ExecutionAuthorizationSource)
                ? AuthorizationSource
                : configuration.ExecutionAuthorizationSource.Trim();
            return new SolidWorksLocalExecutionProfile(
                configuration.RealExecutionAuthorized,
                source,
                configuration.Visible ?? true,
                EmptyToNull(configuration.TemplatePartPath),
                EmptyToNull(configuration.TemplateDrawingPath),
                configuration.ConnectTimeoutSeconds,
                configuration.ExecutionTimeoutSeconds,
                configurationPath,
                configuration.RealExecutionAuthorized && !string.Equals(source, AuthorizationSource, StringComparison.Ordinal)
                    ? new[] { $"local_development_profile_invalid_source: execution_authorization_source must be {AuthorizationSource}." }
                    : Array.Empty<string>());
        }
        catch (JsonException ex)
        {
            return Disabled(configurationPath, $"local_development_profile_invalid: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Disabled(configurationPath, $"local_development_profile_unreadable: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Disabled(configurationPath, $"local_development_profile_unreadable: {ex.Message}");
        }
    }

    public void ApplyToCurrentProcess()
    {
        if (!IsAuthorized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SW_VISIBLE")))
        {
            Environment.SetEnvironmentVariable("SW_VISIBLE", Visible ? "true" : "false");
        }
        Environment.SetEnvironmentVariable("SW_LOCAL_DEVELOPMENT_PROFILE_ENABLED", "true");
        Environment.SetEnvironmentVariable("SW_EXECUTION_AUTHORIZATION_SOURCE", ExecutionAuthorizationSource);
        SetIfPresent("SW_TEMPLATE_PART_PATH", TemplatePartPath);
        SetIfPresent("SW_TEMPLATE_DRAWING_PATH", TemplateDrawingPath);
        SetIfPresent("SW_CONNECT_TIMEOUT_SECONDS", ConnectTimeoutSeconds?.ToString());
        SetIfPresent("SW_EXECUTION_TIMEOUT_SECONDS", ExecutionTimeoutSeconds?.ToString());
    }

    private static SolidWorksLocalExecutionProfile Disabled(string configurationPath, string issue) =>
        new(false, AuthorizationSource, true, null, null, null, null, configurationPath, new[] { issue });

    private static void SetIfPresent(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class LocalConfiguration
    {
        public bool RealExecutionAuthorized { get; init; }
        public string? ExecutionAuthorizationSource { get; init; }
        public bool? Visible { get; init; }
        public string? TemplatePartPath { get; init; }
        public string? TemplateDrawingPath { get; init; }
        public int? ConnectTimeoutSeconds { get; init; }
        public int? ExecutionTimeoutSeconds { get; init; }
    }
}
