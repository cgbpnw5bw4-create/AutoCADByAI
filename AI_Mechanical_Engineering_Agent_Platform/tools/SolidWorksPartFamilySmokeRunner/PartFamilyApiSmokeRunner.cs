using System.Text.Json;
using DomainSchemas;
using SolidWorksWorker;

namespace SolidWorksPartFamilySmokeRunner;

public sealed record PartFamilySmokeRunnerOptions(
    string PartType,
    string OutputRoot,
    bool Enabled,
    bool Visible)
{
    public static PartFamilySmokeRunnerOptions Parse(
        string[] args,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        string? Get(string name) => environment is null
            ? Environment.GetEnvironmentVariable(name)
            : environment.GetValueOrDefault(name);
        var partType = Get("SW_PART_FAMILY_API_SMOKE_PART_TYPE") ?? FlangeBasicDefinition.Type;
        var outputRoot = Get("SW_PART_FAMILY_API_SMOKE_OUTPUT") ??
                         Path.Combine("output", "solidworks", "real", "part_family_api_smoke");
        var visible = ParseBool(Get("SW_VISIBLE"));
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--part-type", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                partType = args[++index];
            }
            else if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                outputRoot = args[++index];
            }
            else if (args[index].Equals("--hidden", StringComparison.OrdinalIgnoreCase))
            {
                visible = false;
            }
        }

        return new PartFamilySmokeRunnerOptions(
            partType,
            outputRoot,
            ParseBool(Get("SW_PART_FAMILY_API_SMOKE_TEST")),
            visible);
    }

    private static bool ParseBool(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
}

public sealed record PartFamilyApiSmokeEvidenceReport(
    string PartType,
    string ExecutionMode,
    bool DedicatedSmokeFlagEnabled,
    bool RealExecutionFlagEnabled,
    bool SolidWorksConnected,
    bool RealCadExecuted,
    IReadOnlyList<PartFamilySmokeArtifactEvidence> Artifacts,
    string GeometryBodyCountStatus,
    string TheoreticalVolumeStatus,
    string ApiEvidence,
    string? FailureStage,
    string FinalStatus,
    IReadOnlyList<string> Issues,
    string EvidenceReportPath,
    DateTimeOffset GeneratedAt);

public sealed record PartFamilySmokeArtifactEvidence(
    string FilePath,
    bool Exists,
    long SizeBytes,
    string Status);

public sealed class PartFamilyApiSmokeRunner
{
    private static readonly IReadOnlyDictionary<string, Func<CADModelSpec>> SpecFactories =
        new Dictionary<string, Func<CADModelSpec>>(StringComparer.OrdinalIgnoreCase)
        {
            [FlangeBasicDefinition.Type] = () => new CADModelSpec(
                "flange-api-smoke",
                FlangeBasicDefinition.Type,
                new Dictionary<string, string>
                {
                    ["outer_diameter_mm"] = "160",
                    ["inner_diameter_mm"] = "60",
                    ["thickness_mm"] = "18",
                    ["bolt_hole_count"] = "6",
                    ["bolt_hole_diameter_mm"] = "14",
                    ["bolt_circle_diameter_mm"] = "115"
                }),
            [ShaftBasicDefinition.Type] = () => new CADModelSpec(
                "shaft-api-smoke",
                ShaftBasicDefinition.Type,
                new Dictionary<string, string>
                {
                    ["diameter_mm"] = "40",
                    ["length_mm"] = "180",
                    ["optional_step_diameters"] = "32,24",
                    ["optional_step_lengths"] = "40,30"
                })
        };

    private readonly Func<SolidWorksWorkerRequest, SolidWorksRuntimeOptions, CancellationToken, Task<SolidWorksWorkerResult>> _execute;

    public PartFamilyApiSmokeRunner(
        Func<SolidWorksWorkerRequest, SolidWorksRuntimeOptions, CancellationToken, Task<SolidWorksWorkerResult>>? execute = null)
    {
        _execute = execute ?? ((request, runtime, cancellationToken) =>
            new RealSolidWorksWorker(options: runtime).ExecuteAsync(request, cancellationToken));
    }

    public async Task<PartFamilyApiSmokeEvidenceReport> RunAsync(
        PartFamilySmokeRunnerOptions runnerOptions,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.Combine(
            Path.GetFullPath(runnerOptions.OutputRoot),
            runnerOptions.PartType,
            $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        var evidencePath = Path.Combine(outputDirectory, "evidence_report.json");
        var runtime = SolidWorksRuntimeOptions.FromEnvironment() with
        {
            Visible = runnerOptions.Visible,
            OutputDirectory = outputDirectory
        };
        var registry = PartTypeRegistry.CreateDefault();
        var builderRegistry = PartFamilyBuilderRegistry.CreateDefault();
        registry.TryGetDefinition(runnerOptions.PartType, out var definition);
        builderRegistry.TryGetBuilder(runnerOptions.PartType, out var builder);

        if (!runnerOptions.Enabled || !runtime.EnableRealExecution)
        {
            return await WriteAsync(new PartFamilyApiSmokeEvidenceReport(
                runnerOptions.PartType,
                builder?.RealExecutionMode ?? "NotStarted",
                runnerOptions.Enabled,
                runtime.EnableRealExecution,
                false,
                false,
                [],
                "NotVerified: smoke execution is disabled",
                "NotVerified: smoke execution is disabled",
                builder?.ApiEvidence ?? "not_available",
                null,
                "Skipped",
                [],
                evidencePath,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        if (definition is null || builder is null || !SpecFactories.TryGetValue(runnerOptions.PartType, out var specFactory))
        {
            return await WriteAsync(new PartFamilyApiSmokeEvidenceReport(
                runnerOptions.PartType,
                builder?.RealExecutionMode ?? "NotStarted",
                true,
                true,
                false,
                false,
                [],
                "NotVerified: unsupported smoke family",
                "NotVerified: unsupported smoke family",
                builder?.ApiEvidence ?? "not_available",
                PartFamilyFailureStages.UnsupportedPartType,
                "Failed",
                [$"unsupported_part_type: the API smoke tool only accepts {FlangeBasicDefinition.Type} or {ShaftBasicDefinition.Type}."],
                evidencePath,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        var planResult = definition.GenerateBuildPlan($"api-smoke-{Guid.NewGuid():N}", specFactory());
        if (!planResult.IsSuccess)
        {
            return await WriteAsync(new PartFamilyApiSmokeEvidenceReport(
                runnerOptions.PartType,
                builder.RealExecutionMode,
                true,
                true,
                false,
                false,
                [],
                "NotVerified: BuildPlan generation failed",
                "NotVerified: BuildPlan generation failed",
                builder.ApiEvidence,
                planResult.FailureStage,
                "Failed",
                planResult.Issues,
                evidencePath,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        var request = new SolidWorksWorkerRequest(
            $"part-family-api-smoke-{Guid.NewGuid():N}",
            planResult.BuildPlan!,
            outputDirectory,
            DryRun: false,
            AllowRealCadExecution: true);
        var result = await _execute(request, runtime, cancellationToken);
        var artifacts = result.GeneratedArtifacts
            .Where(artifact =>
                artifact.ExpectedExtension.Equals(".SLDPRT", StringComparison.OrdinalIgnoreCase) ||
                artifact.ExpectedExtension.Equals(".STEP", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(artifact.FilePath).Equals("build_report.json", StringComparison.OrdinalIgnoreCase))
            .Select(artifact =>
            {
                var info = new FileInfo(artifact.FilePath);
                var valid = info.Exists && info.Length > 0;
                return new PartFamilySmokeArtifactEvidence(
                    artifact.FilePath,
                    info.Exists,
                    info.Exists ? info.Length : 0,
                    valid ? "Passed" : "Failed");
            })
            .ToArray();
        var artifactsPassed = artifacts.Length == 3 && artifacts.All(artifact => artifact.Status == "Passed");
        var passed = result.Status == "Completed" && result.RealCadExecuted && result.RealCadConnected && artifactsPassed;
        var failureStage = passed
            ? null
            : result.FailureStage ?? PartFamilyFailureStages.ArtifactValidationFailed;
        var issues = result.Issues.ToList();
        if (!artifactsPassed)
        {
            issues.Add("artifact_validation_failed: SLDPRT, STEP and build_report.json must all exist and be non-empty.");
        }

        return await WriteAsync(new PartFamilyApiSmokeEvidenceReport(
            runnerOptions.PartType,
            builder.RealExecutionMode,
            true,
            true,
            result.RealCadConnected,
            result.RealCadExecuted,
            artifacts,
            "NotVerified: body count is not exposed by the Phase 1 late-bound smoke contract",
            "NotVerified: theoretical volume is not exposed by the Phase 1 late-bound smoke contract",
            builder.ApiEvidence,
            failureStage,
            passed ? "CandidatePassed" : "Failed",
            issues,
            evidencePath,
            DateTimeOffset.UtcNow), cancellationToken);
    }

    private static async Task<PartFamilyApiSmokeEvidenceReport> WriteAsync(
        PartFamilyApiSmokeEvidenceReport report,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            report.EvidenceReportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }),
            cancellationToken);
        return report;
    }
}
