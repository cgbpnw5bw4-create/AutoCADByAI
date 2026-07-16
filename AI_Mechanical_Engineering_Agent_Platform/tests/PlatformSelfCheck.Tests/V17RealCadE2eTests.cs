using System.Text.Json;
using AgentContracts;
using AgentGatewayHost;
using DomainSchemas;
using PlatformCore;
using SolidWorksWorker;

namespace PlatformSelfCheck.Tests;

public sealed class V17RealCadE2eTests
{
    [Fact]
    public void RouterRecognizesOnlyTheControlledStructuredE2eOperation()
    {
        var router = new SolidWorksWorkflowRouter();
        var context = new AgentContext(
            "task-v17-router",
            new AgentInput(
                "test",
                "test",
                "conversation-v17-router",
                "tester",
                "unrelated natural language",
                Array.Empty<string>(),
                new Dictionary<string, string>
                {
                    ["operation"] = "build_complete_drawing_package",
                    ["part_type"] = "plate_basic_4holes",
                    ["allow_real_cad_execution"] = "true",
                    ["dry_run"] = "false",
                    ["generate_drawing"] = "true",
                    ["generate_dimensions"] = "true",
                    ["generate_title_block"] = "true",
                    ["generate_release_package"] = "true"
                }),
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

        var request = router.TryBuildRequest(context);

        Assert.NotNull(request);
        Assert.Equal(SolidWorksMainWorkflowOperation.BuildCompleteDrawingPackage, request!.Operation);
        Assert.True(request.GenerateDrawing);
        Assert.True(request.GenerateDimensions);
        Assert.True(request.GenerateTitleBlock);
        Assert.True(request.GenerateReleasePackage);
        Assert.True(request.SolidWorksRouterTriggered);
    }

    [Fact]
    public async Task E2eFailsClosedWithoutAnyRealExecutionConfirmation()
    {
        var root = CreateTempDirectory();
        try
        {
            var platform = PlatformBootstrapper.CreateDefault();
            var runner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => new SolidWorksRuntimeOptions(false, false, null, root, 1, 1, null, false));
            var result = await runner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                "request-v17-fail-closed",
                "task-v17-fail-closed",
                root,
                Path.Combine(root, "output", "solidworks", "e2e", "plate_basic_4holes", "run"),
                DryRun: false,
                AllowRealCadExecution: true,
                ModelSpec: SolidWorksWorkflowRouter.CreatePlateBasicFourHolesSpec(),
                Operation: SolidWorksMainWorkflowOperation.BuildCompleteDrawingPackage,
                GenerateDrawing: true,
                GenerateDimensions: true,
                GenerateTitleBlock: true,
                GenerateReleasePackage: true,
                StructuredInputReceived: true,
                ChiefEngineerInvoked: true,
                GatewayInvoked: true,
                SolidWorksRouterTriggered: true));

            var reportPath = Path.Combine(result.OutputDirectory, "reports", "e2e_execution_report.json");
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));

            Assert.Equal("Failed", result.Status);
            Assert.False(result.RealCadExecuted);
            Assert.False(result.QualityGatePassed);
            Assert.Equal("local_execution_authorization_missing", result.FailureStage);
            Assert.Equal("Failed", report.RootElement.GetProperty("final_status").GetString());
            Assert.False(report.RootElement.GetProperty("real_execution_authorized").GetBoolean());
            Assert.Equal("LocalDevelopmentProfile", report.RootElement.GetProperty("execution_authorization_source").GetString());
            Assert.False(report.RootElement.GetProperty("solidworks_launch_attempted").GetBoolean());
            Assert.False(report.RootElement.GetProperty("real_worker_invoked").GetBoolean());
            Assert.False(report.RootElement.GetProperty("all_source_reports_passed").GetBoolean());
            Assert.Equal("NotDeliverable", report.RootElement.GetProperty("deliverable_status").GetString());
            Assert.Equal("local_execution_authorization_missing", report.RootElement.GetProperty("failure_stage").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitSameRunReleaseBlocksFakeExecutionEvidence()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            var sldprt = await WriteAsync(source, "plate_basic_4holes.SLDPRT", "part");
            var step = await WriteAsync(source, "plate_basic_4holes.STEP", "step");
            var drawing = await WriteAsync(source, "plate_basic_4holes_title_block.SLDDRW", "drawing");
            var pdf = await WriteAsync(source, "plate_basic_4holes_title_block.pdf", "pdf");
            var buildReport = await WriteReportAsync(source, "build_report.json");
            var drawingReport = await WriteReportAsync(source, "drawing_report.json");
            var dimensionReport = await WriteReportAsync(source, "dimension_report.json");
            var titleBlockReport = await WriteReportAsync(source, "title_block_report.json");
            var fakeEvidence = ExpectedEvidence(realCadExecuted: false);
            var output = Path.Combine(root, "output", "solidworks", "e2e", "plate_basic_4holes", "run");

            var result = await new SolidWorksE2EReleasePackageBuilder().BuildFromSourcesAsync(
                root,
                new SolidWorksReleasePackageSourceSet(
                    sldprt, step, drawing, pdf, buildReport, drawingReport, dimensionReport, titleBlockReport, fakeEvidence),
                output);
            using var quality = JsonDocument.Parse(await File.ReadAllTextAsync(result.QualityReportPath!));

            Assert.Equal("Failed", result.Status);
            Assert.False(quality.RootElement.GetProperty("all_source_reports_passed").GetBoolean());
            Assert.False(quality.RootElement.GetProperty("real_execution_evidence_passed").GetBoolean());
            Assert.Equal("NotDeliverable", quality.RootElement.GetProperty("deliverable_status").GetString());
            Assert.Equal("real_execution_evidence_failed", quality.RootElement.GetProperty("failure_stage").GetString());
            Assert.False(File.Exists(Path.Combine(output, "reports", "diagnostic_report.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GatewayInvokesChiefEngineerAndWritesTheFailClosedE2eReport()
    {
        var root = CreateTempDirectory();
        try
        {
            var platform = PlatformBootstrapper.CreateDefault(root);
            var output = Path.Combine(root, "output", "solidworks", "e2e", "plate_basic_4holes", "gateway-run");
            var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer", new GatewayMessageRequest(
                "test",
                "test",
                "v17-gateway-conversation",
                "tester",
                "controlled request",
                Array.Empty<string>(),
                new Dictionary<string, string>
                {
                    ["operation"] = "build_complete_drawing_package",
                    ["part_type"] = "plate_basic_4holes",
                    ["allow_real_cad_execution"] = "true",
                    ["dry_run"] = "false",
                    ["generate_drawing"] = "true",
                    ["generate_dimensions"] = "true",
                    ["generate_title_block"] = "true",
                    ["generate_release_package"] = "true",
                    ["structured_input_received"] = "true",
                    ["gateway_invoked"] = "true",
                    ["project_root"] = root,
                    ["solidworks_output_directory"] = output
                }));

            Assert.NotNull(response);
            Assert.Contains(response!.Artifacts, artifact => artifact.Kind == "E2eExecutionReport");
            Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "gateway_request_received");
            Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "solidworks_main_workflow_completed");
            Assert.True(File.Exists(Path.Combine(output, "reports", "e2e_execution_report.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalDevelopmentProfileIsTheOnlyRecognizedV17AuthorizationSource()
    {
        var root = CreateTempDirectory();
        try
        {
            var configurationDirectory = Path.Combine(root, "config");
            Directory.CreateDirectory(configurationDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(configurationDirectory, "solidworks.local.json"),
                "{\"real_execution_authorized\":true,\"execution_authorization_source\":\"LocalDevelopmentProfile\",\"visible\":true}");

            var profile = SolidWorksLocalExecutionProfile.Load(root);

            Assert.True(profile.IsAuthorized);
            Assert.True(profile.RealExecutionAuthorized);
            Assert.True(profile.Visible);
            Assert.Equal("LocalDevelopmentProfile", profile.ExecutionAuthorizationSource);

            await File.WriteAllTextAsync(
                Path.Combine(configurationDirectory, "solidworks.local.json"),
                "{\"real_execution_authorized\":true,\"execution_authorization_source\":\"UntrustedEnvironmentVariable\"}");
            Assert.False(SolidWorksLocalExecutionProfile.Load(root).IsAuthorized);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IReadOnlyList<SolidWorksReleaseExecutionEvidence> ExpectedEvidence(bool realCadExecuted) =>
    [
        new("build", "RealSolidWorksWorker", "RealBuildPlateBasic4Holes", realCadExecuted, true, true, realCadExecuted ? null : "fake_execution", null),
        new("drawing", "RealSolidWorksWorker", "RealDrawingBasicViews", realCadExecuted, true, true, realCadExecuted ? null : "fake_execution", null),
        new("dimension", "RealSolidWorksWorker", "RealDrawingDimensions", realCadExecuted, true, true, realCadExecuted ? null : "fake_execution", null),
        new("title_block", "RealSolidWorksWorker", "RealDrawingTitleBlock", realCadExecuted, true, true, realCadExecuted ? null : "fake_execution", null)
    ];

    private static async Task<string> WriteReportAsync(string directory, string name) =>
        await WriteAsync(directory, name, "{\"final_status\":\"Passed\",\"failure_stage\":null}");

    private static async Task<string> WriteAsync(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai_me_v17_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
