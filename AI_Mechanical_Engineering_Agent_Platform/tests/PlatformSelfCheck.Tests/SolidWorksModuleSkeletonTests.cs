using AgentContracts;
using AgentGatewayHost;
using DomainSchemas;
using PlatformCore;
using PlatformCore.Modules.CADModeling.Agents;
using PlatformCore.Modules.CADModeling.Reviewers;
using PlatformCore.Modules.CADModeling.Skills;
using PlatformCore.Modules.CADModeling.Validators;
using QualityGate;
using SkillContracts;
using SolidWorksWorker;

namespace PlatformSelfCheck.Tests;

public sealed class SolidWorksModuleSkeletonTests
{
    [Fact]
    public async Task SolidWorksBuildPlanSkillGeneratesPlateBasicFourHolesPlan()
    {
        var skill = new SolidWorksBuildPlanSkill();

        var output = await skill.ExecuteAsync(new SkillInput(
            "solidworks-plan-test",
            nameof(CADModelSpec),
            PlateSpec(),
            new Dictionary<string, string> { ["template"] = "plate_basic_4holes" }));

        var plan = Assert.IsType<SolidWorksBuildPlan>(output.Result);
        Assert.Equal(SkillOutputStatus.Completed, output.Status);
        Assert.Equal("SolidWorks", plan.TargetCadSystem);
        Assert.Equal("plate_basic_4holes", plan.PartType);
        Assert.Equal("mm", plan.Unit);
        Assert.Contains(plan.Operations, operation => operation.OperationType == "CreateSketch");
        Assert.Contains(plan.Operations, operation => operation.OperationType == "ExtrudeBoss");
        Assert.Contains(plan.Operations, operation => operation.OperationType == "CutExtrude");
        Assert.Contains(plan.Operations, operation => operation.OperationType == "SavePart");
        Assert.Contains(plan.Operations, operation => operation.OperationType == "ExportStep");
        Assert.Contains(plan.ExpectedArtifacts, artifact => artifact.ExpectedExtension == ".SLDPRT");
        Assert.Contains(plan.ExpectedArtifacts, artifact => artifact.ExpectedExtension == ".STEP");
        Assert.Contains(plan.ExpectedArtifacts, artifact => artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SolidWorksBuildPlanValidatorPassesValidPlanAndRejectsEmptyOperations()
    {
        var skill = new SolidWorksBuildPlanSkill();
        var output = await skill.ExecuteAsync(new SkillInput(
            "solidworks-validator-test",
            nameof(CADModelSpec),
            PlateSpec(),
            new Dictionary<string, string>()));
        var plan = Assert.IsType<SolidWorksBuildPlan>(output.Result);
        var validator = new SolidWorksBuildPlanValidator();

        var valid = validator.Validate(plan);
        var invalid = validator.Validate(plan with { Operations = Array.Empty<SolidWorksOperation>() });

        Assert.True(valid.IsPassed);
        Assert.Empty(valid.Issues);
        Assert.False(invalid.IsPassed);
        Assert.Contains(invalid.Issues, issue => issue.Contains("operations", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FakeSolidWorksWorkerGeneratesFakeArtifactsWithoutRealCad()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", $"fake-worker-test-{Guid.NewGuid():N}");
        try
        {
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                outputRoot);
            var worker = new FakeSolidWorksWorker();

            var result = await worker.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal("Completed", result.Status);
            Assert.Equal("Fake", result.ExecutionMode);
            Assert.False(result.RealCadExecuted);
            Assert.True(request.DryRun);
            Assert.False(request.AllowRealCadExecution);
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith(".SLDPRT.txt", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith(".STEP.txt", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase));
            Assert.All(result.GeneratedArtifacts, artifact => Assert.True(File.Exists(artifact.FilePath)));
            Assert.All(result.GeneratedArtifacts, artifact => Assert.True(new FileInfo(artifact.FilePath).Length > 0));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SolidWorksArtifactValidatorAndReviewerPassDryRunResultThroughQualityGate()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", $"quality-test-{Guid.NewGuid():N}");
        try
        {
            var plan = await CreatePlanAsync();
            var request = new SolidWorksWorkerRequest($"request-{Guid.NewGuid():N}", plan, outputRoot);
            var result = await new FakeSolidWorksWorker().ExecuteAsync(request, CancellationToken.None);
            var artifactReport = new SolidWorksArtifactValidator().Validate(result);
            var review = new SolidWorksBuildPlanReviewer().Review(plan);
            var gate = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder()).Evaluate(review);

            Assert.True(artifactReport.IsPassed);
            Assert.True(review.IsPassed);
            Assert.Equal(GateDecisionResult.Passed, gate.Decision.Result);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CadModelerAgentSuggestsSolidWorksBuildPlanWithoutCallingWorker()
    {
        var agent = new CadModelerAgent();
        var output = await agent.ExecuteAsync(CreateAgentContext("Create a SolidWorks plate modeling plan."));

        Assert.Equal(AgentOutputStatus.Completed, output.Status);
        Assert.Contains("SolidWorksBuildPlan", output.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(output.Logs, log => log.Contains("does not directly call", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(output.Logs, log => log.Contains("Worker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GatewayDoesNotDispatchSolidWorksWorker()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var dispatcher = new AgentMessageDispatcher(platform);

        var response = await dispatcher.DispatchAsync(
            "FakeSolidWorksWorker",
            new GatewayMessageRequest(
                "test",
                "channel",
                "conversation",
                "user",
                "Try to directly call SolidWorks Worker",
                Array.Empty<string>(),
                new Dictionary<string, string>()));

        Assert.Null(response);
    }

    [Fact]
    public async Task SelfCheckReportContainsSolidWorksSkeletonFields()
    {
        var root = FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(root);
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_solidworks", Guid.NewGuid().ToString("N"));

        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, root);

            Assert.True(report.SolidWorksModuleSkeletonEnabled);
            Assert.True(report.SolidWorksBuildPlanSkillRegistered);
            Assert.True(report.SolidWorksBuildPlanGenerated);
            Assert.True(report.SolidWorksWorkerContractExists);
            Assert.True(report.FakeSolidWorksWorkerRegistered);
            Assert.True(report.FakeSolidWorksWorkerDryRunPassed);
            Assert.True(report.SolidWorksBuildPlanValidatorPassed);
            Assert.True(report.SolidWorksArtifactValidatorPassed);
            Assert.True(report.SolidWorksBuildPlanReviewerPassed);
            Assert.True(report.SolidWorksQualityGatePassed);
            Assert.True(report.SolidWorksFakeArtifactsGenerated);
            Assert.True(report.SolidWorksRealCadNotExecuted);
            Assert.True(report.SolidWorksAgentDoesNotCallWorkerDirectly);
            Assert.True(report.GatewayDoesNotCallSolidWorksWorker);
            Assert.True(report.MarkdownChineseCheckPassed);
            Assert.Equal("Passed", report.FinalStatus);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static async Task<SolidWorksBuildPlan> CreatePlanAsync()
    {
        var output = await new SolidWorksBuildPlanSkill().ExecuteAsync(new SkillInput(
            $"task-{Guid.NewGuid():N}",
            nameof(CADModelSpec),
            PlateSpec(),
            new Dictionary<string, string>()));

        return Assert.IsType<SolidWorksBuildPlan>(output.Result);
    }

    private static CADModelSpec PlateSpec() =>
        new(
            "cad-model-spec-plate-basic-4holes",
            "Part",
            "plate_basic_4holes",
            "160 x 80 x 12 mm plate, four diameter 10 mm through holes, rectangular pattern.",
            new Dictionary<string, string>
            {
                ["length_mm"] = "160",
                ["width_mm"] = "80",
                ["thickness_mm"] = "12",
                ["hole_diameter_mm"] = "10",
                ["hole_count"] = "4"
            },
            Array.Empty<string>(),
            new[] { "dry-run only" });

    private static AgentContext CreateAgentContext(string message)
    {
        var input = new AgentInput(
            "test",
            "test-channel",
            $"conversation-{Guid.NewGuid():N}",
            "user",
            message,
            Array.Empty<string>(),
            new Dictionary<string, string>());

        return new AgentContext(
            $"task-{Guid.NewGuid():N}",
            input,
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);
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

        throw new DirectoryNotFoundException("Could not locate project root.");
    }
}
