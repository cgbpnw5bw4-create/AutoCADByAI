using DomainSchemas;
using WorkerContracts;

namespace SolidWorksWorker;

public sealed class FakeSolidWorksWorker : ISolidWorksWorker
{
    public string Name => nameof(FakeSolidWorksWorker);

    public string TargetSystem => "SolidWorks";

    public Task<WorkerOutput> ExecuteAsync(WorkerInput input)
    {
        var artifact = new ArtifactInfo(
            $"solidworks-artifact-{Guid.NewGuid():N}",
            "fake-solidworks-model",
            "model-placeholder",
            $"output/solidworks/{input.TaskId}.json",
            "application/json");

        return Task.FromResult(new WorkerOutput(
            WorkerOutputStatus.Completed,
            new[] { artifact },
            "FakeSolidWorksWorker completed without connecting to SolidWorks.",
            Array.Empty<string>()));
    }
}
