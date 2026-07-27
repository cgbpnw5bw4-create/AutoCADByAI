using System.Text.Json;

namespace SolidWorksWorker;

internal sealed record SolidWorksWorkerLocalExecutionAuthorization(
    bool IsAuthorized,
    string? ConfigurationPath,
    IReadOnlyList<string> Issues)
{
    private const string ConfigurationRelativePath = "config/solidworks.local.json";
    private const string AuthorizationSource = "LocalDevelopmentProfile";

    public static SolidWorksWorkerLocalExecutionAuthorization Load(string outputDirectory)
    {
        string fullOutputDirectory;
        try
        {
            fullOutputDirectory = Path.GetFullPath(outputDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Disabled(null, $"local_development_profile_path_invalid: {ex.Message}");
        }

        var current = new DirectoryInfo(fullOutputDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ConfigurationRelativePath);
            if (File.Exists(candidate))
            {
                return LoadFile(candidate);
            }

            current = current.Parent;
        }

        return Disabled(
            null,
            $"local_development_profile_missing: {ConfigurationRelativePath} was not found in the output directory ancestry.");
    }

    private static SolidWorksWorkerLocalExecutionAuthorization LoadFile(string configurationPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
            var root = document.RootElement;
            var authorized = root.TryGetProperty("real_execution_authorized", out var authorizedElement) &&
                authorizedElement.ValueKind is JsonValueKind.True;
            var source = root.TryGetProperty("execution_authorization_source", out var sourceElement) &&
                sourceElement.ValueKind == JsonValueKind.String
                    ? sourceElement.GetString()
                    : null;
            var sourceIsValid = string.Equals(source, AuthorizationSource, StringComparison.Ordinal);
            var issues = new List<string>();
            if (!authorized)
            {
                issues.Add("local_development_profile_not_authorized: real_execution_authorized must be true.");
            }
            if (!sourceIsValid)
            {
                issues.Add($"local_development_profile_invalid_source: execution_authorization_source must be {AuthorizationSource}.");
            }

            return new SolidWorksWorkerLocalExecutionAuthorization(
                authorized && sourceIsValid,
                Path.GetFullPath(configurationPath),
                issues);
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

    private static SolidWorksWorkerLocalExecutionAuthorization Disabled(string? path, string issue) =>
        new(false, path, [issue]);
}
