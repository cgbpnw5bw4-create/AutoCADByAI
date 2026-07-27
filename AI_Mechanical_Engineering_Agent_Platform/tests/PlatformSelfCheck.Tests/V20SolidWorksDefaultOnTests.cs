using AgentContracts;
using DomainSchemas;
using PlatformCore;

namespace PlatformSelfCheck.Tests;

public sealed class V20SolidWorksDefaultOnTests
{
    [Fact]
    public async Task LocalInteractivePolicySelectsRealWorkerBeforeWorkerInvocation()
    {
        var projectRoot = FindProjectRoot();
        var output = Path.Combine(projectRoot, "output", "solidworks", $"v20-real-route-{Guid.NewGuid():N}");
        try
        {
            var platform = PlatformBootstrapper.CreateDefault(projectRoot);
            var options = SolidWorksRuntimeOptions.FromEnvironment(
                new Dictionary<string, string?>(),
                isUnitTestEnvironment: false);
            var runner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => options);
            var invalidSpec = PlateSpec() with { PartType = "unregistered_v20_part_family" };

            var result = await runner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                $"v20-real-route-{Guid.NewGuid():N}",
                $"v20-real-route-task-{Guid.NewGuid():N}",
                projectRoot,
                output,
                DryRun: false,
                AllowRealCadExecution: false,
                ModelSpec: invalidSpec));

            Assert.True(options.RealExecutionDefaultEnabled);
            Assert.True(options.ShouldUseRealWorker(dryRun: false));
            Assert.Equal("RealSolidWorksWorker", result.WorkerName);
            Assert.DoesNotContain(result.WorkflowResult.Steps, step => step.StepId == "solidworks-worker-execution");
            Assert.DoesNotContain(result.Issues, issue =>
                issue.Contains("real_execution_" + "confirmation_missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("dry_run")]
    [InlineData("disable_env")]
    [InlineData("ci")]
    [InlineData("unit_test")]
    [InlineData("force_fake")]
    public async Task DisablePoliciesSelectFakeWorkerAndStillPassQualityGate(string mode)
    {
        var projectRoot = FindProjectRoot();
        var output = Path.Combine(projectRoot, "output", "solidworks", $"v20-fake-route-{mode}-{Guid.NewGuid():N}");
        try
        {
            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var isUnitTest = false;
            var dryRun = false;
            switch (mode)
            {
                case "dry_run":
                    dryRun = true;
                    break;
                case "disable_env":
                    environment["SW_DISABLE_REAL_EXECUTION"] = "true";
                    break;
                case "ci":
                    environment["CI"] = "true";
                    break;
                case "unit_test":
                    isUnitTest = true;
                    break;
                case "force_fake":
                    environment["SW_FORCE_FAKE_WORKER"] = "true";
                    break;
            }

            var options = SolidWorksRuntimeOptions.FromEnvironment(environment, isUnitTest);
            var platform = PlatformBootstrapper.CreateDefault(projectRoot);
            var runner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => options);
            var result = await runner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                $"v20-fake-route-{Guid.NewGuid():N}",
                $"v20-fake-route-task-{Guid.NewGuid():N}",
                projectRoot,
                output,
                DryRun: dryRun,
                AllowRealCadExecution: false,
                ModelSpec: PlateSpec()));

            Assert.False(options.ShouldUseRealWorker(dryRun));
            Assert.Equal("FakeSolidWorksWorker", result.WorkerName);
            Assert.Equal("Fake", result.ExecutionMode);
            Assert.False(result.RealCadConnected);
            Assert.False(result.RealCadExecuted);
            Assert.True(result.QualityGatePassed);
            Assert.Equal(GateDecisionResult.Passed.ToString(), result.QualityGateDecision);
            Assert.Contains(result.WorkflowResult.Steps, step =>
                step.StepId == "solidworks-artifact-quality-gate" &&
                step.GateDecision?.Result == GateDecisionResult.Passed);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public void RuntimeDefaultsAreVisibleAndIgnoreLegacyEnableFlag()
    {
        var options = SolidWorksRuntimeOptions.FromEnvironment(
            new Dictionary<string, string?>
            {
                ["SW_ENABLE_REAL_EXECUTION"] = "false"
            },
            isUnitTestEnvironment: false);

        Assert.True(options.RealExecutionDefaultEnabled);
        Assert.True(options.EnableRealExecution);
        Assert.True(options.VisibleModeDefault);
        Assert.True(options.Visible);
        Assert.False(options.DisableRealExecution);
    }

    private static CADModelSpec PlateSpec() =>
        new(
            $"v20-plate-spec-{Guid.NewGuid():N}",
            PlateBasic4HolesDefinition.Type,
            new Dictionary<string, string>
            {
                ["length_mm"] = "160",
                ["width_mm"] = "80",
                ["thickness_mm"] = "12",
                ["hole_count"] = "4",
                ["hole_diameter_mm"] = "10"
            },
            material: "Q235",
            outputRequirements: ["SLDPRT", "STEP", "build_report.json"]);

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Project root was not found.");
    }
}
