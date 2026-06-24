using DomainSchemas;
using WorkerContracts;

namespace PlatformCore;

public sealed class PlaceholderWorker : IWorker
{
    public PlaceholderWorker(string name, string targetSystem)
    {
        Name = name;
        TargetSystem = targetSystem;
    }

    public string Name { get; }

    public string TargetSystem { get; }

    public Task<WorkerOutput> ExecuteAsync(WorkerInput input)
    {
        var artifact = new ArtifactInfo(
            $"artifact-{Guid.NewGuid():N}",
            $"{Name}-placeholder-output",
            "placeholder",
            $"output/{Name}/{input.TaskId}.json",
            "application/json");

        var output = new WorkerOutput(
            WorkerOutputStatus.Completed,
            new[] { artifact },
            $"{Name} did not call {TargetSystem}; placeholder execution only.",
            Array.Empty<string>());

        return Task.FromResult(output);
    }
}
