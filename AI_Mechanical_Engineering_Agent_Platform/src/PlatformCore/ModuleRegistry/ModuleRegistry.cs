using ModuleContracts;

namespace PlatformCore;

public sealed class ModuleRegistry
{
    private readonly Dictionary<string, ModuleManifest> _modules = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ModuleManifest manifest)
    {
        _modules[manifest.Name] = manifest;
    }

    public ModuleManifest? GetByName(string name) =>
        _modules.TryGetValue(name, out var module) ? module : null;

    public IReadOnlyList<ModuleManifest> GetAll() =>
        _modules.Values.OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}
