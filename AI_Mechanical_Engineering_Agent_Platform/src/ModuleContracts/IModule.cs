namespace ModuleContracts;

public interface IModule
{
    string Name { get; }

    string Version { get; }

    string Description { get; }

    IReadOnlyList<ModuleCapability> Capabilities { get; }

    IReadOnlyList<string> Agents { get; }

    IReadOnlyList<string> Skills { get; }

    IReadOnlyList<string> Workers { get; }

    IReadOnlyList<string> Validators { get; }

    IReadOnlyList<string> Reviewers { get; }
}
