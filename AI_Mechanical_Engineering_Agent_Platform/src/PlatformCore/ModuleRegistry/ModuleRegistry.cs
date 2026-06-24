using ModuleContracts;

namespace PlatformCore;

public sealed class ModuleRegistry
{
    private readonly Dictionary<string, ModuleManifest> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sources = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ModuleManifest manifest, string source = "manual")
    {
        _modules[manifest.Name] = manifest;
        _sources[manifest.Name] = source;
    }

    public ModuleManifest? GetByName(string name) =>
        _modules.TryGetValue(name, out var module) ? module : null;

    public IReadOnlyList<ModuleManifest> GetAll() =>
        _modules.Values.OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<ModuleManifestRegistration> GetSources() =>
        _sources
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new ModuleManifestRegistration(pair.Key, pair.Value))
            .ToArray();
}

public sealed record ModuleManifestRegistration(string ModuleName, string Source);
