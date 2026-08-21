using System.Text.Json;
using AgentContracts;
using DomainSchemas;
using PlatformCore;
using SolidWorksWorker;

namespace PlatformSelfCheck.Tests;

public sealed class V19MainWorkflowTests
{
    [Theory]
    [InlineData("flange_basic", "real_cad_flange_request.json")]
    [InlineData("shaft_basic", "real_cad_shaft_request.json")]
    public void StructuredExamplesUseCanonicalCadModelSpec(string partType, string fileName)
    {
        var path = Path.Combine(FindProjectRoot(), "examples", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal(
            SolidWorksE2eCliContract.PartFamilyReleasePackageOperation,
            root.GetProperty("operation").GetString());
        var spec = root.GetProperty("cad_model_spec");
        Assert.Equal(partType, spec.GetProperty("model_type").GetString());
        Assert.Equal("mm", spec.GetProperty("unit").GetString());
        Assert.Equal(JsonValueKind.Object, spec.GetProperty("parameters").ValueKind);
        Assert.Equal(JsonValueKind.Array, spec.GetProperty("sketches").ValueKind);
        Assert.Equal(JsonValueKind.Array, spec.GetProperty("features").ValueKind);
        Assert.False(spec.GetProperty("drawing_requirements").GetProperty("generate_drawing").GetString() == "true");
        Assert.Equal("true", spec.GetProperty("drawing_requirements").GetProperty("generate_release_package").GetString());
    }

    [Fact]
    public void RouterPreservesNestedFlangeSpecAndBuildOnlyOperation()
    {
        var spec = FlangeSpec();
        var context = new AgentContext(
            "task-v19-router",
            new AgentInput(
                "test",
                "test",
                "conversation-v19-router",
                "tester",
                "controlled flange request",
                [],
                new Dictionary<string, string>
                {
                    ["operation"] = SolidWorksE2eCliContract.PartFamilyReleasePackageOperation,
                    ["cad_model_spec_json"] = JsonSerializer.Serialize(spec, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    }),
                    ["dry_run"] = "false",
                    ["generate_release_package"] = "true"
                }),
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

        var request = new SolidWorksWorkflowRouter().TryBuildRequest(context);

        Assert.NotNull(request);
        Assert.Equal(SolidWorksMainWorkflowOperation.BuildPartFamilyReleasePackage, request!.Operation);
        Assert.Equal(FlangeBasicDefinition.Type, request.ModelSpec!.PartType);
        Assert.Equal("115", request.ModelSpec.Dimensions["bolt_circle_diameter_mm"]);
        Assert.False(request.GenerateDrawing);
        Assert.True(request.GenerateReleasePackage);
        Assert.True(request.SolidWorksRouterTriggered);
    }

    [Fact]
    public async Task BuildOnlyMainWorkflowUsesFakeAndCannotDeliverWhenRuntimeDisablesRealCad()
    {
        var root = CreateTempDirectory();
        try
        {
            var platform = PlatformBootstrapper.CreateDefault(root);
            var runner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => new SolidWorksRuntimeOptions(false, false, null, root, 1, 1, MainWorkflowExecutionEnabled: false));
            var output = Path.Combine(root, "output", "solidworks", "e2e", FlangeBasicDefinition.Type, "run");
            var result = await runner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                "request-v19-fail-closed",
                "task-v19-fail-closed",
                root,
                output,
                DryRun: false,
                AllowRealCadExecution: true,
                ModelSpec: FlangeSpec(),
                Operation: SolidWorksMainWorkflowOperation.BuildPartFamilyReleasePackage,
                GenerateReleasePackage: true,
                StructuredInputReceived: true,
                ChiefEngineerInvoked: true,
                GatewayInvoked: true,
                SolidWorksRouterTriggered: true));

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(output, "reports", "e2e_execution_report.json")));

            Assert.Equal("Failed", result.Status);
            Assert.Equal("source_artifacts_missing", result.FailureStage);
            Assert.Equal("FakeSolidWorksWorker", result.WorkerName);
            Assert.False(result.RealCadExecuted);
            Assert.Equal(FlangeBasicDefinition.Type, report.RootElement.GetProperty("part_type").GetString());
            Assert.Equal("BuildPartFamilyReleasePackage", report.RootElement.GetProperty("operation").GetString());
            Assert.False(report.RootElement.GetProperty("solidworks_launch_attempted").GetBoolean());
            Assert.False(report.RootElement.GetProperty("real_execution_policy_enabled").GetBoolean());
            Assert.Equal("NotDeliverable", report.RootElement.GetProperty("deliverable_status").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SingleStageNonPlateDoesNotRequireLegacyRequestConfirmationWhenFakeIsForced()
    {
        var root = CreateTempDirectory();
        try
        {
            var platform = PlatformBootstrapper.CreateDefault(root);
            var runner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => new SolidWorksRuntimeOptions(
                    false,
                    true,
                    null,
                    root,
                    1,
                    1,
                    MainWorkflowExecutionEnabled: false,
                    DisableRealExecution: false,
                    IsUnitTestEnvironment: true,
                    ForceFakeWorker: true));
            var result = await runner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                "request-v19-single-stage-fail-closed",
                "task-v19-single-stage-fail-closed",
                root,
                Path.Combine(root, "output", "solidworks", FlangeBasicDefinition.Type),
                DryRun: false,
                AllowRealCadExecution: false,
                ModelSpec: FlangeSpec(),
                Stage: SolidWorksMainWorkflowStage.BuildPartFamily,
                Operation: SolidWorksMainWorkflowOperation.BuildPlate));

            Assert.Equal("Completed", result.Status);
            Assert.Null(result.FailureStage);
            Assert.Equal("FakeSolidWorksWorker", result.WorkerName);
            Assert.False(result.RealCadConnected);
            Assert.False(result.RealCadExecuted);
            Assert.NotEmpty(result.ArtifactPaths);
            Assert.Contains(result.WorkflowResult.Steps, step =>
                step.StepId == "solidworks-worker-execution" && step.Status == WorkflowStepStatus.Passed);
            Assert.Contains(result.WorkflowResult.Steps, step =>
                step.StepId == "solidworks-artifact-quality-gate" && step.GateDecision?.Result == GateDecisionResult.Passed);
            Assert.DoesNotContain(result.Logs, log => log.Contains("connection_started", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildOnlyReleasePackageUsesCurrentFlangeRunAndOmitsDrawingArtifacts()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            var part = Write(source, "flange_basic.SLDPRT", "solidworks-part");
            var step = Write(source, "flange_basic.STEP", "step-data");
            var report = Write(source, "build_report.json", "{\"final_status\":\"Passed\",\"failure_stage\":null}");
            var output = Path.Combine(root, "output", "solidworks", "e2e", "flange_basic", "run");
            var result = await new SolidWorksE2EReleasePackageBuilder().BuildFromSourcesAsync(
                root,
                new SolidWorksReleasePackageSourceSet(
                    part,
                    step,
                    DrawingPath: null,
                    PdfPath: null,
                    BuildReportPath: report,
                    DrawingReportPath: null,
                    DimensionReportPath: null,
                    TitleBlockReportPath: null,
                    ExecutionEvidence:
                    [
                        new SolidWorksReleaseExecutionEvidence(
                            "build", "RealSolidWorksWorker", PartFamilyExecutionModes.GenericFeatureGraph,
                            true, true, true, null, report)
                    ],
                    RequireRealExecutionEvidence: true,
                    PartType: FlangeBasicDefinition.Type,
                    RequestId: "request-v19-release",
                    RequireDrawingDeliverables: false),
                output);

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath!));
            using var quality = JsonDocument.Parse(await File.ReadAllTextAsync(result.QualityReportPath!));

            Assert.Equal("Completed", result.Status);
            Assert.Equal(2, manifest.RootElement.GetProperty("artifacts").GetArrayLength());
            Assert.Equal(1, manifest.RootElement.GetProperty("reports").GetArrayLength());
            Assert.Equal(FlangeBasicDefinition.Type, manifest.RootElement.GetProperty("part_name").GetString());
            Assert.False(manifest.RootElement.GetProperty("requires_drawing_deliverables").GetBoolean());
            Assert.Equal("Deliverable", quality.RootElement.GetProperty("deliverable_status").GetString());
            Assert.False(quality.RootElement.GetProperty("pdf_exists").GetBoolean());
            Assert.True(File.Exists(Path.Combine(output, "artifacts", "flange_basic.SLDPRT")));
            Assert.True(File.Exists(Path.Combine(output, "artifacts", "flange_basic.STEP")));
            Assert.False(File.Exists(Path.Combine(output, "artifacts", "flange_basic.SLDDRW")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CADModelSpec FlangeSpec() => new(
        "flange-v19",
        FlangeBasicDefinition.Type,
        new Dictionary<string, string>
        {
            ["outer_diameter_mm"] = "160",
            ["inner_diameter_mm"] = "60",
            ["thickness_mm"] = "18",
            ["bolt_hole_count"] = "6",
            ["bolt_hole_diameter_mm"] = "14",
            ["bolt_circle_diameter_mm"] = "115"
        });

    private static string Write(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai_me_v19_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the project root.");
    }
}
