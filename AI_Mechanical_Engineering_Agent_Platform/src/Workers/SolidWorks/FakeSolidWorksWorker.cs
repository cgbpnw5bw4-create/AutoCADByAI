using System.Text.Json;
using DomainSchemas;
using WorkerContracts;

namespace SolidWorksWorker;

public sealed class FakeSolidWorksWorker : ISolidWorksWorker
{
    public string Name => nameof(FakeSolidWorksWorker);

    public string TargetSystem => "SolidWorks";

    public async Task<SolidWorksWorkerResult> ExecuteAsync(
        SolidWorksWorkerRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new List<string>();
        if (!request.DryRun)
        {
            issues.Add("FakeSolidWorksWorker only supports dry_run=true.");
        }

        if (request.AllowRealCadExecution)
        {
            issues.Add("FakeSolidWorksWorker never allows real CAD execution.");
        }

        var outputRoot = Path.GetFullPath(request.OutputDirectory);
        var artifactsDirectory = Path.Combine(outputRoot, "artifacts");
        var reportsDirectory = Path.Combine(outputRoot, "reports");
        var logsDirectory = Path.Combine(outputRoot, "logs");
        Directory.CreateDirectory(artifactsDirectory);
        Directory.CreateDirectory(reportsDirectory);
        Directory.CreateDirectory(logsDirectory);

        var partPath = Path.Combine(artifactsDirectory, "fake_plate_basic_4holes.SLDPRT.txt");
        var stepPath = Path.Combine(artifactsDirectory, "fake_plate_basic_4holes.STEP.txt");
        var reportPath = Path.Combine(reportsDirectory, "build_report.json");
        var logPath = Path.Combine(logsDirectory, "fake_solidworks_worker.log");

        await File.WriteAllTextAsync(
            partPath,
            "Fake SolidWorks part placeholder for plate_basic_4holes. No real CAD data is stored here.",
            cancellationToken);
        await File.WriteAllTextAsync(
            stepPath,
            "Fake STEP placeholder for plate_basic_4holes. No real CAD export was executed.",
            cancellationToken);

        var buildReport = new
        {
            request_id = request.RequestId,
            plan_id = request.BuildPlan.PlanId,
            execution_mode = "Fake",
            dry_run = request.DryRun,
            allow_real_cad_execution = request.AllowRealCadExecution,
            real_cad_executed = false,
            operations = request.BuildPlan.Operations.Select(operation => operation.OperationType).ToArray(),
            generated_at = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(buildReport, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        await File.WriteAllTextAsync(
            logPath,
            "FakeSolidWorksWorker completed without launching SolidWorks, COM or SldWorks.Application.",
            cancellationToken);

        var artifacts = new[]
        {
            Artifact("generated-part", "Part", partPath, ".SLDPRT", "Fake text placeholder for a SolidWorks part artifact."),
            Artifact("generated-step", "Step", stepPath, ".STEP", "Fake text placeholder for a STEP artifact."),
            Artifact("generated-build-report", "BuildReport", reportPath, ".json", "Dry-run build report."),
            Artifact("generated-log", "Log", logPath, ".log", "Dry-run worker execution log.")
        };

        return new SolidWorksWorkerResult(
            request.RequestId,
            issues.Count == 0 ? "Completed" : "Rejected",
            artifacts,
            new[]
            {
                "FakeSolidWorksWorker generated dry-run artifacts.",
                "No SolidWorks process was started.",
                "No COM call was executed.",
                "SldWorks.Application was not used."
            },
            issues,
            ExecutionMode: "Fake",
            RealCadExecuted: false);
    }

    public Task<WorkerOutput> ExecuteAsync(WorkerInput input) =>
        ExecuteAsync(input, CancellationToken.None);

    public async Task<WorkerOutput> ExecuteAsync(WorkerInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (input.Payload is not SolidWorksWorkerRequest request)
        {
            return new WorkerOutput(
                WorkerOutputStatus.Rejected,
                Array.Empty<ArtifactInfo>(),
                "FakeSolidWorksWorker requires SolidWorksWorkerRequest payload.",
                new[] { "payload must be SolidWorksWorkerRequest." });
        }

        var result = await ExecuteAsync(request, cancellationToken);
        var workerArtifacts = result.GeneratedArtifacts
            .Select(artifact => new ArtifactInfo(
                artifact.ArtifactId,
                artifact.ArtifactType,
                artifact.Description,
                artifact.FilePath,
                InferContentType(artifact.ExpectedExtension)))
            .ToArray();

        return new WorkerOutput(
            result.Status == "Completed" ? WorkerOutputStatus.Completed : WorkerOutputStatus.Rejected,
            workerArtifacts,
            string.Join(Environment.NewLine, result.Logs),
            result.Issues);
    }

    private static SolidWorksArtifact Artifact(
        string artifactId,
        string artifactType,
        string path,
        string expectedExtension,
        string description)
    {
        var fullPath = Path.GetFullPath(path);
        var fileInfo = new FileInfo(fullPath);
        return new SolidWorksArtifact(
            artifactId,
            artifactType,
            fullPath,
            expectedExtension,
            fileInfo.Exists,
            fileInfo.Exists ? fileInfo.Length : 0,
            description);
    }

    private static string InferContentType(string extension) =>
        extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? "application/json"
            : "text/plain";
}
