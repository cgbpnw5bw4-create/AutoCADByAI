using System.Text.Json;

namespace PlatformCore;

/// <summary>
/// Optional local SolidWorks settings. This file never grants or denies real execution;
/// SolidWorksRuntimeOptions owns the V2.0 execution policy.
/// </summary>
public sealed record SolidWorksLocalExecutionProfile(
    bool Visible,
    string? TemplatePartPath,
    string? TemplateDrawingPath,
    int? ConnectTimeoutSeconds,
    int? ExecutionTimeoutSeconds,
    string ConfigurationPath,
    IReadOnlyList<string> Issues)
{
    public const string ConfigurationRelativePath = "config/solidworks.local.json";

    public static SolidWorksLocalExecutionProfile Load(string projectRoot)
    {
        var configurationPath = Path.GetFullPath(Path.Combine(projectRoot, ConfigurationRelativePath));
        if (!File.Exists(configurationPath))
        {
            return Defaults(configurationPath);
        }

        try
        {
            var configuration = JsonSerializer.Deserialize<LocalConfiguration>(
                File.ReadAllText(configurationPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });
            if (configuration is null)
            {
                return Defaults(configurationPath);
            }

            return new SolidWorksLocalExecutionProfile(
                configuration.Visible ?? true,
                EmptyToNull(configuration.TemplatePartPath),
                EmptyToNull(configuration.TemplateDrawingPath),
                configuration.ConnectTimeoutSeconds,
                configuration.ExecutionTimeoutSeconds,
                configurationPath,
                Array.Empty<string>());
        }
        catch (JsonException ex)
        {
            return Invalid(configurationPath, $"solidworks_local_profile_invalid: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Invalid(configurationPath, $"solidworks_local_profile_unreadable: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Invalid(configurationPath, $"solidworks_local_profile_unreadable: {ex.Message}");
        }
    }

    public void ApplyToCurrentProcess()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SW_VISIBLE")))
        {
            Environment.SetEnvironmentVariable("SW_VISIBLE", Visible ? "true" : "false");
        }

        SetIfPresent("SW_TEMPLATE_PART_PATH", TemplatePartPath);
        SetIfPresent("SW_TEMPLATE_DRAWING_PATH", TemplateDrawingPath);
        SetIfPresent("SW_CONNECT_TIMEOUT_SECONDS", ConnectTimeoutSeconds?.ToString());
        SetIfPresent("SW_EXECUTION_TIMEOUT_SECONDS", ExecutionTimeoutSeconds?.ToString());
    }

    private static SolidWorksLocalExecutionProfile Invalid(string configurationPath, string issue) =>
        new(true, null, null, null, null, configurationPath, [issue]);

    private static SolidWorksLocalExecutionProfile Defaults(string configurationPath) =>
        new(true, null, null, null, null, configurationPath, Array.Empty<string>());

    private static void SetIfPresent(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class LocalConfiguration
    {
        public bool? Visible { get; init; }
        public string? TemplatePartPath { get; init; }
        public string? TemplateDrawingPath { get; init; }
        public int? ConnectTimeoutSeconds { get; init; }
        public int? ExecutionTimeoutSeconds { get; init; }
    }
}
