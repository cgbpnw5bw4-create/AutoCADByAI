using System.Text.Json;
using DomainSchemas;
using WorkerContracts;

namespace SolidWorksWorker;

public sealed class FakeSolidWorksWorker : ISolidWorksWorker
{
    private readonly PartTypeRegistry _partTypeRegistry;
    private readonly PartFamilyBuilderRegistry _builderRegistry;

    public FakeSolidWorksWorker()
        : this(null, null)
    {
    }

    public FakeSolidWorksWorker(
        PartTypeRegistry? partTypeRegistry,
        PartFamilyBuilderRegistry? builderRegistry)
    {
        _partTypeRegistry = partTypeRegistry ?? PartTypeRegistry.CreateDefault();
        _builderRegistry = builderRegistry ?? PartFamilyBuilderRegistry.CreateDefault();
    }

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

        if (!_partTypeRegistry.TryGetDefinition(request.BuildPlan.PartType, out var definition))
        {
            return Rejected(
                request,
                PartFamilyFailureStages.UnsupportedPartType,
                $"unsupported_part_type: {request.BuildPlan.PartType} is not registered.");
        }

        if (!_builderRegistry.TryGetBuilder(request.BuildPlan.PartType, out var builder))
        {
            return Rejected(
                request,
                PartFamilyFailureStages.PartFamilyBuilderMissing,
                $"part_family_builder_missing: no dry-run builder is registered for {request.BuildPlan.PartType}.");
        }

        var planIssues = definition.ReviewBuildPlan(request.BuildPlan);
        if (planIssues.Count > 0)
        {
            return Rejected(
                request,
                PartFamilyFailureStages.InvalidParameterValue,
                planIssues.Select(issue => $"invalid_parameter_value: {issue}").ToArray());
        }

        if (issues.Count > 0)
        {
            return new SolidWorksWorkerResult(
                request.RequestId,
                "Rejected",
                Array.Empty<SolidWorksArtifact>(),
                ["FakeSolidWorksWorker rejected unsafe execution flags before artifact creation."],
                issues,
                ExecutionMode: "Fake",
                FailureStage: PartFamilyFailureStages.InvalidParameterValue);
        }

        var outputRoot = Path.GetFullPath(request.OutputDirectory);
        var artifactsDirectory = Path.Combine(outputRoot, "artifacts");
        var reportsDirectory = Path.Combine(outputRoot, "reports");
        var logsDirectory = Path.Combine(outputRoot, "logs");
        Directory.CreateDirectory(reportsDirectory);
        Directory.CreateDirectory(logsDirectory);

        var familyResult = await builder.BuildDryRunAsync(request, artifactsDirectory, cancellationToken);
        if (!string.Equals(familyResult.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidWorksWorkerResult(
                request.RequestId,
                familyResult.Status,
                familyResult.Artifacts,
                familyResult.Logs,
                familyResult.Issues,
                ExecutionMode: "Fake",
                FailureStage: familyResult.FailureStage ?? builder.FailureStage);
        }

        var reportPath = Path.Combine(reportsDirectory, "build_report.json");
        var logPath = Path.Combine(logsDirectory, "fake_solidworks_worker.log");
        var buildReport = new
        {
            request_id = request.RequestId,
            plan_id = request.BuildPlan.PlanId,
            part_type = request.BuildPlan.PartType,
            part_family_definition = definition.GetType().Name,
            part_family_builder = builder.GetType().Name,
            parameter_schema = definition.ParameterSchema,
            api_evidence = builder.ApiEvidence,
            dimensions = request.BuildPlan.Dimensions,
            material = request.BuildPlan.Material,
            features = request.BuildPlan.Features,
            output_requirements = request.BuildPlan.OutputRequirements,
            drawing_requirements = request.BuildPlan.DrawingRequirements,
            execution_options = request.BuildPlan.ExecutionOptions,
            execution_mode = "Fake",
            dry_run = request.DryRun,
            allow_real_cad_execution = request.AllowRealCadExecution,
            real_cad_executed = false,
            operations = request.BuildPlan.Operations.Select(operation => new
            {
                operation.OperationId,
                operation.OperationType,
                operation.Parameters
            }).ToArray(),
            failure_stage = (string?)null,
            final_status = "Passed",
            generated_at = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(buildReport, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        await File.WriteAllTextAsync(
            logPath,
            $"FakeSolidWorksWorker completed {request.BuildPlan.PartType} through {builder.GetType().Name} without launching SolidWorks, COM or SldWorks.Application.",
            cancellationToken);

        var artifacts = familyResult.Artifacts
            .Concat([
                Artifact("generated-build-report", "BuildReport", reportPath, ".json", "Family-aware dry-run build report."),
                Artifact("generated-log", "Log", logPath, ".log", "Dry-run worker execution log.")
            ])
            .ToArray();
        var logs = familyResult.Logs
            .Concat([
                $"FakeSolidWorksWorker generated {request.BuildPlan.PartType} dry-run artifacts.",
                "No SolidWorks process was started.",
                "No COM call was executed.",
                "SldWorks.Application was not used."
            ])
            .ToArray();

        return new SolidWorksWorkerResult(
            request.RequestId,
            "Completed",
            artifacts,
            logs,
            Array.Empty<string>(),
            ExecutionMode: "Fake",
            RealCadExecuted: false,
            RealCadConnected: false,
            PreflightReport: null,
            FailureStage: null);
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

    private static SolidWorksWorkerResult Rejected(
        SolidWorksWorkerRequest request,
        string failureStage,
        params string[] issues) =>
        new(
            request.RequestId,
            "Rejected",
            Array.Empty<SolidWorksArtifact>(),
            ["FakeSolidWorksWorker rejected the request before artifact creation."],
            issues,
            ExecutionMode: "Fake",
            RealCadExecuted: false,
            RealCadConnected: false,
            PreflightReport: null,
            FailureStage: failureStage);

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
