using AgentContracts;
using DomainSchemas;
using PlatformCore;
using SolidWorksWorker;

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

    [Fact]
    public async Task UnavailableExecutionEnvironmentFailsBeforeComConnection()
    {
        var options = SolidWorksRuntimeOptions.FromEnvironment(
            new Dictionary<string, string?>(),
            isUnitTestEnvironment: false);
        var sessionManager = new CountingSessionManager();
        var worker = new RealSolidWorksWorker(
            sessionManager,
            options,
            null,
            null,
            null,
            null,
            null,
            new UnavailableExecutionEnvironmentProbe());
        var request = new SolidWorksWorkerRequest(
            $"v20-unavailable-environment-{Guid.NewGuid():N}",
            BuildPlan(),
            Path.GetTempPath(),
            DryRun: false,
            ConnectionSmokeTestOnly: true);

        var result = await worker.ExecuteAsync(request);

        Assert.True(options.ShouldUseRealWorker(dryRun: false));
        Assert.Equal("Failed", result.Status);
        Assert.Equal(SolidWorksExecutionEnvironmentProbe.FailureStage, result.FailureStage);
        Assert.False(result.RealCadConnected);
        Assert.False(result.RealCadExecuted);
        Assert.Equal(0, sessionManager.ConnectAttempts);
        Assert.Contains(result.Issues, issue =>
            issue.StartsWith("solidworks_com_registration_missing:", StringComparison.Ordinal));
    }

    [Fact]
    public void SelfCheckControlledContextAlwaysRoutesAsDryRun()
    {
        var context = V17RealCadE2eSelfCheck.CreateControlledContext(
            FindProjectRoot(),
            Path.Combine(Path.GetTempPath(), $"v20-self-check-{Guid.NewGuid():N}"));

        var request = new SolidWorksWorkflowRouter().TryBuildRequest(context);

        Assert.NotNull(request);
        Assert.True(request.DryRun);
        Assert.False(request.AllowRealCadExecution);
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

    private static SolidWorksBuildPlan BuildPlan() =>
        new(
            $"v20-build-plan-{Guid.NewGuid():N}",
            $"v20-spec-{Guid.NewGuid():N}",
            "SolidWorks",
            PlateBasic4HolesDefinition.Type,
            "mm",
            Array.Empty<SolidWorksOperation>(),
            Array.Empty<SolidWorksArtifact>(),
            Array.Empty<string>(),
            Array.Empty<string>());

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

    private sealed class UnavailableExecutionEnvironmentProbe : ISolidWorksExecutionEnvironmentProbe
    {
        public SolidWorksExecutionEnvironmentProbeResult Probe() =>
            new(
                false,
                ["solidworks_com_registration_missing: test probe reports SolidWorks unavailable."]);
    }

    private sealed class CountingSessionManager : ISolidWorksSessionManager
    {
        public int ConnectAttempts { get; private set; }

        public Task<SolidWorksSessionConnectionResult> ConnectAsync(
            SolidWorksRuntimeOptions options,
            CancellationToken cancellationToken = default)
        {
            ConnectAttempts++;
            return Task.FromResult(new SolidWorksSessionConnectionResult(
                true,
                "test",
                Array.Empty<string>(),
                Array.Empty<string>()));
        }

        public Task<T> ExecuteWithApplicationAsync<T>(
            Func<object, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default,
            int? executionTimeoutSeconds = null) =>
            action(new object(), cancellationToken);

        public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
