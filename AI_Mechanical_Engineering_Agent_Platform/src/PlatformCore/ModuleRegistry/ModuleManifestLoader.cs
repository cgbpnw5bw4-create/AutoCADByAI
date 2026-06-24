using ModuleContracts;

namespace PlatformCore;

public sealed class ModuleManifestLoader
{
    public ModuleManifestLoadResult LoadFromModulesDirectory(string modulesRoot)
    {
        var manifests = new List<ModuleManifest>();
        var errors = new List<ModuleManifestLoadError>();

        if (!Directory.Exists(modulesRoot))
        {
            errors.Add(new ModuleManifestLoadError(modulesRoot, "Modules directory was not found."));
            return new ModuleManifestLoadResult(manifests, errors);
        }

        foreach (var moduleDirectory in Directory.GetDirectories(modulesRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(moduleDirectory, "module.yaml");
            if (!File.Exists(manifestPath))
            {
                errors.Add(new ModuleManifestLoadError(manifestPath, "module.yaml was not found."));
                continue;
            }

            try
            {
                manifests.Add(ParseManifest(manifestPath));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                errors.Add(new ModuleManifestLoadError(manifestPath, ex.Message));
            }
        }

        return new ModuleManifestLoadResult(manifests, errors);
    }

    private static ModuleManifest ParseManifest(string manifestPath)
    {
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentListKey = null;

        foreach (var rawLine in File.ReadAllLines(manifestPath))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (currentListKey is null)
                {
                    throw new InvalidDataException($"List item has no parent key in {manifestPath}: {trimmed}");
                }

                lists[currentListKey].Add(Unquote(trimmed[2..].Trim()));
                continue;
            }

            var separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex <= 0)
            {
                throw new InvalidDataException($"Unsupported yaml line in {manifestPath}: {trimmed}");
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();

            if (value.Length == 0)
            {
                lists[key] = new List<string>();
                currentListKey = key;
                continue;
            }

            currentListKey = null;
            if (value == "[]")
            {
                lists[key] = new List<string>();
                continue;
            }

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                lists[key] = value[1..^1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Unquote)
                    .ToList();
                continue;
            }

            scalars[key] = Unquote(value);
        }

        var name = RequiredScalar(scalars, manifestPath, "name");
        var version = RequiredScalar(scalars, manifestPath, "version");
        var description = RequiredScalar(scalars, manifestPath, "description");
        var schemas = GetList(lists, "schemas");
        var skills = GetList(lists, "skills");
        var capabilities = skills.Count > 0
            ? skills.Select(skill => new ModuleCapability(skill, description, schemas, new[] { "StructuredPlatformResult" })).ToArray()
            : new[] { new ModuleCapability(name, description, schemas, new[] { "StructuredPlatformResult" }) };

        return new ModuleManifest(
            name,
            version,
            description,
            capabilities,
            Array.Empty<ModuleDependency>(),
            GetList(lists, "agents"),
            skills,
            GetList(lists, "workers"),
            GetList(lists, "validators"),
            GetList(lists, "reviewers"));
    }

    private static string RequiredScalar(Dictionary<string, string> scalars, string manifestPath, string key)
    {
        if (scalars.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidDataException($"Required scalar '{key}' was not found in {manifestPath}.");
    }

    private static IReadOnlyList<string> GetList(Dictionary<string, List<string>> lists, string key) =>
        lists.TryGetValue(key, out var values) ? values : Array.Empty<string>();

    private static string Unquote(string value) =>
        value.Trim().Trim('"').Trim('\'');
}

public sealed record ModuleManifestLoadResult(
    IReadOnlyList<ModuleManifest> Manifests,
    IReadOnlyList<ModuleManifestLoadError> Errors);

public sealed record ModuleManifestLoadError(
    string Path,
    string Message);
