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
    public void CliContractAcceptsOnlyTheControlledInputSyntax()
    {
        Assert.True(SolidWorksE2eCliContract.IsInvocation(
            ["run-cad-workflow", "--input", "examples/real_cad_plate_request.json"]));
        Assert.False(SolidWorksE2eCliContract.IsInvocation(
            ["run-cad-workflow", "--input"]));
        Assert.False(SolidWorksE2eCliContract.IsInvocation(
            ["run-cad-workflow", "--unexpected", "examples/real_cad_plate_request.json"]));
    }

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
    public async Task E2eUsesFakeAndCannotDeliverWhenRuntimePolicyDisablesRealExecution()
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
            Assert.Equal("source_artifacts_missing", result.FailureStage);
            Assert.Equal("FakeSolidWorksWorker", result.WorkerName);
            Assert.Equal("Failed", report.RootElement.GetProperty("final_status").GetString());
            Assert.False(report.RootElement.GetProperty("real_execution_policy_enabled").GetBoolean());
            Assert.Equal("SolidWorksRuntimeOptions", report.RootElement.GetProperty("execution_policy_source").GetString());
            Assert.False(report.RootElement.GetProperty("solidworks_launch_attempted").GetBoolean());
            Assert.False(report.RootElement.GetProperty("real_worker_invoked").GetBoolean());
            Assert.False(report.RootElement.GetProperty("all_source_reports_passed").GetBoolean());
            Assert.Equal("NotDeliverable", report.RootElement.GetProperty("deliverable_status").GetString());
            Assert.Equal("source_artifacts_missing", report.RootElement.GetProperty("failure_stage").GetString());
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
    public async Task LocalProfileOnlySuppliesOptionalRuntimeSettings()
    {
        var root = CreateTempDirectory();
        try
        {
            var configurationDirectory = Path.Combine(root, "config");
            Directory.CreateDirectory(configurationDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(configurationDirectory, "solidworks.local.json"),
                "{\"visible\":true}");

            var profile = SolidWorksLocalExecutionProfile.Load(root);

            Assert.True(profile.Visible);
            Assert.Empty(profile.Issues);

            await File.WriteAllTextAsync(
                Path.Combine(configurationDirectory, "solidworks.local.json"),
                "{\"visible\":false}");
            Assert.False(SolidWorksLocalExecutionProfile.Load(root).Visible);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalProfileDoesNotSetLegacyRuntimeConfirmationVariables()
    {
        var root = CreateTempDirectory();
        var variables = new[]
        {
            "SW_ENABLE_REAL_EXECUTION",
            "SW_REAL_MAIN_WORKFLOW_TEST",
            "SW_VISIBLE",
            "SW_LOCAL_DEVELOPMENT_PROFILE_ENABLED",
            "SW_EXECUTION_AUTHORIZATION_SOURCE"
        };
        var previous = variables.ToDictionary(variable => variable, Environment.GetEnvironmentVariable);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "config"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "config", "solidworks.local.json"),
                "{\"visible\":false}");
            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }

            SolidWorksLocalExecutionProfile.Load(root).ApplyToCurrentProcess();

            Assert.Null(Environment.GetEnvironmentVariable("SW_ENABLE_REAL_EXECUTION"));
            Assert.Null(Environment.GetEnvironmentVariable("SW_REAL_MAIN_WORKFLOW_TEST"));
            Assert.Equal("false", Environment.GetEnvironmentVariable("SW_VISIBLE"));
            Assert.Null(Environment.GetEnvironmentVariable("SW_LOCAL_DEVELOPMENT_PROFILE_ENABLED"));
            Assert.Null(Environment.GetEnvironmentVariable("SW_EXECUTION_AUTHORIZATION_SOURCE"));

            Environment.SetEnvironmentVariable("SW_VISIBLE", "true");
            SolidWorksLocalExecutionProfile.Load(root).ApplyToCurrentProcess();
            Assert.Equal("true", Environment.GetEnvironmentVariable("SW_VISIBLE"));
        }
        finally
        {
            foreach (var (variable, value) in previous)
            {
                Environment.SetEnvironmentVariable(variable, value);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunGitAsyncReturnsNoRevisionWhenGitReportsAnError()
    {
        var root = CreateTempDirectory();
        try
        {
            var result = await PlatformSelfCheckRunner.RunGitAsync(root, "rev-parse", "--verify", "HEAD");

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void V17SelfCheckUsesBehavioralEvidenceInsteadOfSourceTextMatches()
    {
        var sourcePath = Path.Combine(FindProjectRoot(), "src", "PlatformCore", "PlatformSelfCheckRunner.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("cliProgramText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("routerText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e2eRunnerText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e2eReleaseBuilderText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("localAuthorizationProfileText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("smokeRunnerText", source, StringComparison.Ordinal);
        var v20AStart = source.IndexOf(
            "private static V20AGenericCadModelSpecSelfCheckResult RunV20AGenericCadModelSpecChecks",
            StringComparison.Ordinal);
        var v20AEnd = source.IndexOf(
            "private static V20BFeatureHandlerSelfCheckResult RunV20BFeatureHandlerChecks",
            v20AStart,
            StringComparison.Ordinal);
        Assert.True(v20AStart >= 0 && v20AEnd > v20AStart);
        Assert.DoesNotContain("File.ReadAllText", source[v20AStart..v20AEnd], StringComparison.Ordinal);

        var v18Start = source.IndexOf("RunV18PartFamilyChecksAsync", StringComparison.Ordinal);
        var v18End = source.IndexOf("private static V19PartFamilySelfCheckResult", v18Start, StringComparison.Ordinal);
        Assert.True(v18Start >= 0 && v18End > v18Start);
        Assert.DoesNotContain("File.ReadAllText", source[v18Start..v18End], StringComparison.Ordinal);
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
