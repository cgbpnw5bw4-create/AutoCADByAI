using DomainSchemas;
using WorkerContracts;

namespace AutoCADWorker;

public sealed class FakeAutoCADWorker : IAutoCADWorker
{
    public string Name => nameof(FakeAutoCADWorker);

    public string TargetSystem => "AutoCAD";

    public Task<WorkerOutput> ExecuteAsync(WorkerInput input)
    {
        var artifact = new ArtifactInfo(
            $"autocad-artifact-{Guid.NewGuid():N}",
            "fake-autocad-parse-result",
            "drawing-placeholder",
            $"output/autocad/{input.TaskId}.json",
            "application/json");

        return Task.FromResult(new WorkerOutput(
            WorkerOutputStatus.Completed,
            new[] { artifact },
            "FakeAutoCADWorker completed without connecting to AutoCAD.",
            Array.Empty<string>()));
    }
}
