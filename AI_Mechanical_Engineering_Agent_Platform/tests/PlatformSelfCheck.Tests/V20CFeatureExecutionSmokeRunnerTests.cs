using System.Text.Json;
using DomainSchemas;
using SolidWorksFeatureExecutionSmokeRunner;
using SolidWorksWorker;
using SolidWorksWorker.Features;

namespace PlatformSelfCheck.Tests;

public sealed class V20CFeatureExecutionSmokeRunnerTests
{
    [Fact]
    public async Task DefaultDisabledRunnerDoesNotInvokeExecution()
    {
        var root = TempRoot();
        var invocations = 0;
        try
        {
            var runner = new FeatureExecutionSmokeRunner(
                (_, _) =>
                {
                    Interlocked.Increment(ref invocations);
                    throw new InvalidOperationException("Real CAD execution must remain disabled.");
                },
                EnabledRuntime);

            var report = await runner.RunAsync(
                new FeatureExecutionSmokeRunnerOptions(
                    InputPath: null,
                    OutputRoot: root,
                    Enabled: false,
                    Visible: false),
                CancellationToken.None);

            Assert.Equal("Skipped", report.FinalStatus);
            Assert.Equal(0, invocations);
            Assert.True(report.CandidateOnly);
            Assert.False(report.MainWorkflowAccepted);
            Assert.False(report.QualityGatePassed);
            Assert.Equal("NotDeliverable", report.DeliverableStatus);
            Assert.True(File.Exists(report.ReportPath));
            Assert.EndsWith(
                Path.Combine(Path.GetFileName(Path.GetDirectoryName(report.ReportPath))!, "feature_execution_report.json"),
                report.ReportPath,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task RuntimeDisableAlsoPreventsExecution()
    {
        var root = TempRoot();
        var invocations = 0;
        try
        {
            var runner = new FeatureExecutionSmokeRunner(
                (_, _) =>
                {
                    Interlocked.Increment(ref invocations);
                    throw new InvalidOperationException("Real CAD execution must remain disabled.");
                },
                () => EnabledRuntime() with { EnableRealExecution = false });

            var report = await runner.RunAsync(
                new FeatureExecutionSmokeRunnerOptions(
                    InputPath: null,
                    OutputRoot: root,
                    Enabled: true,
                    Visible: false),
                CancellationToken.None);

            Assert.Equal("Skipped", report.FinalStatus);
            Assert.Equal(0, invocations);
            Assert.True(report.DedicatedSmokeFlagEnabled);
            Assert.False(report.RealExecutionAllowed);
            Assert.False(report.SolidWorksConnected);
            Assert.False(report.RealCadExecuted);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task CandidateRunNormalizesArtifactsButNeverClaimsMainWorkflowAcceptance()
    {
        var root = TempRoot();
        var inputPath = Path.Combine(root, "feature-input.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(CreateInputSpec()));

        try
        {
            var runner = new FeatureExecutionSmokeRunner(
                async (invocation, cancellationToken) =>
                {
                    var partPath = Path.Combine(invocation.WorkingDirectory, "source.SLDPRT");
                    var stepPath = Path.Combine(invocation.WorkingDirectory, "source.STEP");
                    var buildReportPath = Path.Combine(invocation.WorkingDirectory, "build_report.json");
                    await File.WriteAllTextAsync(partPath, "real-part-candidate", cancellationToken);
                    await File.WriteAllTextAsync(stepPath, "real-step-candidate", cancellationToken);
                    var featureReports = invocation.BuildPlan.Operations
                        .Where(operation =>
                            !operation.OperationType.Equals("SavePart", StringComparison.OrdinalIgnoreCase) &&
                            !operation.OperationType.Equals("ExportStep", StringComparison.OrdinalIgnoreCase))
                        .Select(operation =>
                        {
                            var adaptation = FeatureHandlerPlanAdapter.Adapt(operation);
                            return new FeatureHandlerReport(
                                adaptation.Feature!.FeatureId,
                                adaptation.Feature.FeatureType,
                                "diagnostic-handler",
                                "diagnostic_candidate",
                                null,
                                [],
                                [],
                                "solidworks.real-feature-adapter",
                                "2.0-c.1",
                                ResultObjectValidated: true,
                                RebuildPassed: true,
                                GeometryChangeValidated: true);
                        })
                        .ToArray();
                    await File.WriteAllTextAsync(
                        buildReportPath,
                        JsonSerializer.Serialize(
                            new { feature_handler_reports = featureReports },
                            new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                            }),
                        cancellationToken);

                    return new FeatureExecutionSmokeBuildOutcome(
                        SolidWorksConnected: true,
                        SolidWorksVersion: "test-version",
                        BuildResult: new PartFamilyBuildResult(
                            "Completed",
                            [
                                Artifact(partPath, ".SLDPRT"),
                                Artifact(stepPath, ".STEP"),
                                Artifact(buildReportPath, ".json")
                            ],
                            [],
                            [],
                            "RealBuildGenericFeatureGraph",
                            RealCadExecuted: true),
                        FailureStage: null,
                        Logs: [],
                        Issues: []);
                },
                EnabledRuntime);

            var report = await runner.RunAsync(
                new FeatureExecutionSmokeRunnerOptions(
                    inputPath,
                    root,
                    Enabled: true,
                    Visible: false),
                CancellationToken.None);

            Assert.True(
                report.FinalStatus == "CandidatePassed",
                string.Join(Environment.NewLine, report.Issues));
            Assert.True(report.Model.Exists);
            Assert.True(report.Step.Exists);
            Assert.Equal("model.SLDPRT", Path.GetFileName(report.Model.FilePath));
            Assert.Equal("model.STEP", Path.GetFileName(report.Step.FilePath));
            Assert.Equal(
                Path.GetDirectoryName(report.ReportPath),
                Path.GetDirectoryName(report.Model.FilePath));
            Assert.False(report.MainWorkflowAccepted);
            Assert.False(report.QualityGatePassed);
            Assert.Equal("NotDeliverable", report.DeliverableStatus);
            Assert.All(report.FeatureHandlerReports, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.FeatureId));
                Assert.False(string.IsNullOrWhiteSpace(item.FeatureType));
                Assert.True(item.ResultObjectValidated);
                Assert.True(item.RebuildPassed);
                Assert.True(item.GeometryChangeValidated);
            });
        }
        finally
        {
            Delete(root);
        }
    }

    private static CADModelSpec CreateInputSpec() =>
        new(
            "model",
            PlateBasic4HolesDefinition.Type,
            "mm",
            parameters: new Dictionary<string, string>
            {
                ["length_mm"] = "100",
                ["width_mm"] = "60",
                ["thickness_mm"] = "10",
                ["hole_count"] = "4",
                ["hole_diameter_mm"] = "10"
            },
            referenceGeometry: new Dictionary<string, string> { ["primary_plane"] = "TopPlane" },
            sketches:
            [
                new SketchDefinition(
                    "profile",
                    "TopPlane",
                    [
                        new SketchEntity(
                            "rectangle",
                            SketchEntityTypes.Rectangle,
                            new Dictionary<string, string>
                            {
                                ["center_x_mm"] = "0",
                                ["center_y_mm"] = "0",
                                ["width_mm"] = "100",
                                ["height_mm"] = "60"
                            })
                    ],
                    [],
                    new Dictionary<string, string>(),
                    1)
            ],
            features:
            [
                new FeatureDefinition(
                    "boss",
                    FeatureTypes.ExtrudeBoss,
                    new Dictionary<string, string>
                    {
                        ["depth_mm"] = "10",
                        ["direction"] = "blind"
                    },
                    dependencies: [],
                    referencedSketches: ["profile"],
                    referencedFeatures: [],
                    executionOrder: 1)
            ],
            outputRequirements: ["SLDPRT", "STEP", "feature_execution_report.json"]);

    private static SolidWorksRuntimeOptions EnabledRuntime() =>
        new(
            EnableRealExecution: true,
            Visible: false,
            TemplatePartPath: "unused-in-injected-test",
            OutputDirectory: Path.GetTempPath(),
            ConnectTimeoutSeconds: 1,
            ExecutionTimeoutSeconds: 1,
            MainWorkflowExecutionEnabled: true,
            RealExecutionDefaultEnabled: true,
            DisableRealExecution: false,
            IsCiEnvironment: false,
            IsUnitTestEnvironment: false,
            VisibleModeDefault: true,
            ForceFakeWorker: false);

    private static SolidWorksArtifact Artifact(string path, string extension)
    {
        var info = new FileInfo(path);
        return new(
            $"artifact-{Guid.NewGuid():N}",
            "SmokeCandidate",
            path,
            extension,
            info.Exists,
            info.Exists ? info.Length : 0,
            "Injected test artifact.");
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"v20-c-feature-smoke-{Guid.NewGuid():N}");

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
