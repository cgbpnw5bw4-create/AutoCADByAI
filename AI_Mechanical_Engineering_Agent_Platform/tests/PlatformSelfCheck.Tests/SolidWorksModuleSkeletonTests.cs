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
using SolidWorksSmokeRunner;
using SolidWorksWorker;
using SolidWorksWorker.Diagnostics;
using System.Text.Json;
using WorkerContracts;

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
    public void PlatformBootstrapperRegistersRealFakeSolidWorksWorker()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());

        var worker = Assert.Single(platform.WorkerRegistry.GetAll(), registered => registered.Name == nameof(FakeSolidWorksWorker));

        Assert.IsType<FakeSolidWorksWorker>(worker);
        Assert.Contains(platform.AuditLog.GetEntries(), entry =>
            entry.Category == "worker" &&
            entry.Action == "registered" &&
            entry.Message.Contains("SolidWorks dry-run worker", StringComparison.OrdinalIgnoreCase));
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
    public async Task SolidWorksMainWorkflowRunnerUsesFakeWorkerByDefaultAndRequiresBothRealFlags()
    {
        var projectRoot = FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(projectRoot);
        var outputBase = Path.Combine(projectRoot, "output", "solidworks", $"main-workflow-test-{Guid.NewGuid():N}");
        var defaultOptions = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>());

        try
        {
            var runner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => defaultOptions);

            var defaultResult = await runner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                $"request-{Guid.NewGuid():N}",
                $"task-{Guid.NewGuid():N}",
                projectRoot,
                Path.Combine(outputBase, "default"),
                DryRun: true,
                AllowRealCadExecution: false));
            var envOnlyResult = await new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => defaultOptions with { EnableRealExecution = true })
                .ExecuteAsync(new SolidWorksMainWorkflowRequest(
                    $"request-{Guid.NewGuid():N}",
                    $"task-{Guid.NewGuid():N}",
                    projectRoot,
                    Path.Combine(outputBase, "env-only"),
                    DryRun: false,
                    AllowRealCadExecution: false));
            var requestOnlyResult = await runner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                $"request-{Guid.NewGuid():N}",
                $"task-{Guid.NewGuid():N}",
                projectRoot,
                Path.Combine(outputBase, "request-only"),
                DryRun: false,
                AllowRealCadExecution: true));

            Assert.Equal("Completed", defaultResult.Status);
            Assert.Equal("Fake", defaultResult.ExecutionMode);
            Assert.False(defaultResult.RealCadExecuted);
            Assert.True(defaultResult.QualityGatePassed);
            Assert.Contains(defaultResult.WorkflowResult.Steps, step => step.StepId == "solidworks-worker-execution");
            Assert.NotEmpty(defaultResult.ArtifactPaths);
            Assert.Equal("Fake", envOnlyResult.ExecutionMode);
            Assert.False(envOnlyResult.RealCadExecuted);
            Assert.Equal("Fake", requestOnlyResult.ExecutionMode);
            Assert.False(requestOnlyResult.RealCadExecuted);
        }
        finally
        {
            if (Directory.Exists(outputBase))
            {
                Directory.Delete(outputBase, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksWorkflowRouterUsesStructuredInputsAndIgnoresGenericSolidWorksMentions()
    {
        var projectRoot = FindProjectRoot();
        var router = new SolidWorksWorkflowRouter();
        var genericMention = new AgentContext(
            $"task-{Guid.NewGuid():N}",
            new AgentInput(
                "test",
                "test-channel",
                $"conversation-{Guid.NewGuid():N}",
                "user",
                "Discuss SolidWorks automation risks without launching CAD.",
                Array.Empty<string>(),
                new Dictionary<string, string>
                {
                    ["project_root"] = projectRoot
                }),
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);
        var structuredRequest = new AgentContext(
            $"task-{Guid.NewGuid():N}",
            new AgentInput(
                "test",
                "test-channel",
                $"conversation-{Guid.NewGuid():N}",
                "user",
                "Build the requested CAD model through the main workflow.",
                Array.Empty<string>(),
                new Dictionary<string, string>
                {
                    ["project_root"] = projectRoot,
                    ["solidworks_output_directory"] = Path.Combine(projectRoot, "output", "solidworks", $"router-test-{Guid.NewGuid():N}"),
                    ["cad_model_type"] = "plate_basic_4holes",
                    ["length_mm"] = "200",
                    ["width_mm"] = "90",
                    ["thickness_mm"] = "14",
                    ["hole_diameter_mm"] = "12",
                    ["hole_count"] = "4",
                    ["dry_run"] = "false",
                    ["allow_real_cad_execution"] = "true"
                }),
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

        Assert.Null(router.TryBuildRequest(genericMention));
        var request = router.TryBuildRequest(structuredRequest);

        Assert.NotNull(request);
        Assert.False(request!.DryRun);
        Assert.True(request.AllowRealCadExecution);
        Assert.Equal("plate_basic_4holes", request.ModelSpec!.ModelType);
        Assert.Equal("200", request.ModelSpec.Parameters["length_mm"]);
        Assert.Equal("90", request.ModelSpec.Parameters["width_mm"]);
        Assert.Equal("14", request.ModelSpec.Parameters["thickness_mm"]);
        Assert.Equal("12", request.ModelSpec.Parameters["hole_diameter_mm"]);
    }

    [Fact]
    public async Task SolidWorksMainWorkflowRunnerFailsClosedWhenWorkerFails()
    {
        var projectRoot = FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(projectRoot);
        platform.WorkerRegistry.Register(new FailingSolidWorksWorker());
        var outputBase = Path.Combine(projectRoot, "output", "solidworks", $"main-workflow-failure-test-{Guid.NewGuid():N}");

        try
        {
            var runner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>()));

            var result = await runner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                $"request-{Guid.NewGuid():N}",
                $"task-{Guid.NewGuid():N}",
                projectRoot,
                outputBase,
                DryRun: true,
                AllowRealCadExecution: false));

            Assert.Equal("Failed", result.Status);
            Assert.Equal("Failed", result.QualityGateDecision);
            Assert.False(result.QualityGatePassed);
            Assert.False(result.RealCadExecuted);
            Assert.Equal("worker_execution_failed", result.FailureStage);
            Assert.Contains(result.Issues, issue => issue.Contains("intentional_worker_failure", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(result.WorkflowResult.FailureReport);
            Assert.Equal("solidworks-worker-execution", result.WorkflowResult.FailureReport!.FailedStepId);
            Assert.Contains(result.WorkflowResult.Steps, step =>
                step.StepId == "solidworks-worker-execution" &&
                step.Status == WorkflowStepStatus.Failed);
            Assert.DoesNotContain(result.WorkflowResult.Steps, step => step.StepId == "solidworks-artifact-quality-gate");
        }
        finally
        {
            if (Directory.Exists(outputBase))
            {
                Directory.Delete(outputBase, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ChiefEngineerOrchestratorInvokesSolidWorksMainWorkflowForPlateRequest()
    {
        var projectRoot = FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(projectRoot);
        var outputBase = Path.Combine(projectRoot, "output", "solidworks", $"chief-main-workflow-test-{Guid.NewGuid():N}");
        try
        {
            var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer")!;
            var input = new AgentInput(
                "test",
                "test-channel",
                $"conversation-{Guid.NewGuid():N}",
                "user",
                "Run SolidWorks plate_basic_4holes main workflow.",
                Array.Empty<string>(),
                new Dictionary<string, string>
                {
                    ["project_root"] = projectRoot,
                    ["solidworks_output_directory"] = outputBase
                });

            var output = await chiefEngineer.ExecuteAsync(new AgentContext(
                $"task-{Guid.NewGuid():N}",
                input,
                new Dictionary<string, object?>(),
                DateTimeOffset.UtcNow));

            Assert.Equal(AgentOutputStatus.Completed, output.Status);
            Assert.Contains("real_cad_executed=False", output.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("quality_gate_passed=True", output.Message, StringComparison.OrdinalIgnoreCase);
            var reportArtifact = Assert.Single(output.Artifacts, artifact =>
                artifact.Name.Equals("solidworks-main-workflow-report", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("False", reportArtifact.Metadata!["real_cad_executed"]);
            Assert.Equal("True", reportArtifact.Metadata!["quality_gate_passed"]);
            Assert.Contains(output.Artifacts, artifact => artifact.Path.StartsWith(outputBase, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "quality_gate_after_solidworks_main_workflow");
        }
        finally
        {
            if (Directory.Exists(outputBase))
            {
                Directory.Delete(outputBase, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SolidWorksArtifactValidatorRejectsArtifactsOutsideConfiguredOutputRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_artifact_path_test", Guid.NewGuid().ToString("N"));
        var allowedRoot = Path.Combine(root, "output", "solidworks");
        var otherRoot = Path.Combine(root, "other", "output", "solidworks");
        try
        {
            Directory.CreateDirectory(otherRoot);
            var outsideArtifactPath = Path.Combine(otherRoot, "fake_plate_basic_4holes.SLDPRT.txt");
            await File.WriteAllTextAsync(outsideArtifactPath, "outside configured root");
            var result = new SolidWorksWorkerResult(
                $"request-{Guid.NewGuid():N}",
                "Completed",
                new[]
                {
                    new SolidWorksArtifact(
                        "outside-artifact",
                        "Part",
                        outsideArtifactPath,
                        ".SLDPRT",
                        true,
                        new FileInfo(outsideArtifactPath).Length,
                        "Outside configured output root."),
                    new SolidWorksArtifact(
                        "build-report",
                        "BuildReport",
                        outsideArtifactPath.Replace(".SLDPRT.txt", "build_report.json"),
                        ".json",
                        true,
                        1,
                        "Fake build report marker.")
                },
                Array.Empty<string>(),
                Array.Empty<string>());
            await File.WriteAllTextAsync(result.GeneratedArtifacts[1].FilePath, "{}");

            var report = new SolidWorksArtifactValidator(allowedRoot).Validate(result);

            Assert.False(report.IsPassed);
            Assert.Contains(report.Issues, issue => issue.Contains("configured output/solidworks", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FakeSolidWorksWorkerWorkerInputOverloadPropagatesCancellationToken()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", $"cancel-test-{Guid.NewGuid():N}");
        var request = new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            await CreatePlanAsync(),
            outputRoot);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        IWorker worker = new FakeSolidWorksWorker();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            worker.ExecuteAsync(
                new WorkerInput(
                    "cancel-test",
                    "solidworks-dry-run",
                    request,
                    new Dictionary<string, string>()),
                cancellation.Token));
    }

    [Fact]
    public async Task PlaceholderWorkerDefaultOverloadHonorsPreCanceledToken()
    {
        IWorker worker = new PlaceholderWorker("FakeSolidWorksWorker", "SolidWorks");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            worker.ExecuteAsync(
                new WorkerInput(
                    "placeholder-cancel-test",
                    "solidworks-dry-run",
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                cancellation.Token));
    }

    [Fact]
    public async Task SolidWorksBuildPlanValidatorDoesNotTreatNonCriticalTextAsFatal()
    {
        var plan = await CreatePlanAsync();
        var validator = new SolidWorksBuildPlanValidator();

        var nonCritical = validator.Validate(plan with
        {
            Operations = new[]
            {
                new SolidWorksOperation(
                    "non-critical-operation",
                    "This operation name contains non-critical text.",
                    "Top Plane",
                    new Dictionary<string, string>(),
                    Array.Empty<string>(),
                    "Should be rejected without marking the issue fatal.")
            }
        });
        var nonRetryable = validator.Validate(new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            plan,
            Path.Combine("output", "solidworks", "validator-test"),
            DryRun: true,
            AllowRealCadExecution: true));

        Assert.False(nonCritical.IsPassed);
        Assert.False(nonCritical.HasFatalError);
        Assert.False(nonRetryable.IsPassed);
        Assert.True(nonRetryable.HasFatalError);
    }

    [Fact]
    public void PlatformPathResolverThrowsWhenProjectRootCannotBeLocated()
    {
        var root = Path.Combine(Path.GetTempPath(), "platform_path_resolver_test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var exception = Assert.Throws<DirectoryNotFoundException>(() =>
                PlatformPathResolver.FindProjectRoot(root));

            Assert.Contains("Could not locate project root", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
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
    public async Task SolidWorksEnvironmentValidatorGeneratesSkippedPreflightWithoutComWhenRealExecutionDisabled()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "solidworks_preflight_test", Guid.NewGuid().ToString("N"));
        var request = new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            await CreatePlanAsync(),
            outputRoot);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "false",
            ["SW_OUTPUT_DIRECTORY"] = outputRoot,
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
        });
        var validator = new SolidWorksEnvironmentValidator();

        var report = validator.ValidateEnvironment(request, options);

        Assert.Equal("Skipped", report.FinalStatus);
        Assert.False(report.RealExecutionEnabled);
        Assert.False(report.SolidWorksApplicationConnectable);
        Assert.True(report.OutputDirectoryWritable);
        Assert.Contains(report.Issues, issue => issue.Contains("missing_user_safety_confirmation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RealSolidWorksWorkerRejectsWhenRequestOrEnvironmentSafetySwitchesAreMissing()
    {
        var disabledOptions = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "false",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
        });
        var enabledOptions = disabledOptions with { EnableRealExecution = true };
        var sessionManager = new CountingSolidWorksSessionManager();
        var workerWithDisabledEnvironment = new RealSolidWorksWorker(sessionManager, disabledOptions);
        var workerWithEnabledEnvironment = new RealSolidWorksWorker(sessionManager, enabledOptions);
        var defaultRequest = new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            await CreatePlanAsync(),
            Path.Combine(Path.GetTempPath(), "solidworks_real_worker_reject_default", Guid.NewGuid().ToString("N")));

        var defaultResult = await workerWithDisabledEnvironment.ExecuteAsync(defaultRequest, CancellationToken.None);
        var missingRequestFlagResult = await workerWithEnabledEnvironment.ExecuteAsync(
            defaultRequest with { DryRun = false, AllowRealCadExecution = false },
            CancellationToken.None);
        var missingEnvironmentFlagResult = await workerWithDisabledEnvironment.ExecuteAsync(
            defaultRequest with { DryRun = false, AllowRealCadExecution = true },
            CancellationToken.None);

        Assert.Equal("Rejected", defaultResult.Status);
        Assert.Equal("RealPreflightOnly", defaultResult.ExecutionMode);
        Assert.False(defaultResult.RealCadExecuted);
        Assert.False(defaultResult.RealCadConnected);
        Assert.Contains(defaultResult.Issues, issue => issue.Contains("dry_run_mode_enabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(defaultResult.Issues, issue => issue.Contains("real_cad_execution_not_enabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(defaultResult.Issues, issue => issue.Contains("missing_user_safety_confirmation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(missingRequestFlagResult.Issues, issue => issue.Contains("real_cad_execution_not_enabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(missingEnvironmentFlagResult.Issues, issue => issue.Contains("missing_user_safety_confirmation", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, sessionManager.ConnectAttempts);
    }

    [Fact]
    public async Task RealSolidWorksWorkerOnlyEntersConnectionPathWhenAllSafetySwitchesAreEnabled()
    {
        var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "true",
            ["SW_VISIBLE"] = "false",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
        });
        var worker = new RealSolidWorksWorker(sessionManager, options);
        var request = new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            await CreatePlanAsync(),
            Path.Combine(Path.GetTempPath(), "solidworks_real_worker_connection", Guid.NewGuid().ToString("N")),
            DryRun: false,
            AllowRealCadExecution: true,
            ConnectionSmokeTestOnly: true);

        var result = await worker.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(1, sessionManager.ConnectAttempts);
        Assert.Equal("Completed", result.Status);
        Assert.Equal("RealConnectionSmokeTest", result.ExecutionMode);
        Assert.True(result.RealCadConnected);
        Assert.False(result.RealCadExecuted);
        Assert.NotEqual("RealBuild", result.ExecutionMode);
        Assert.False(worker.SupportsGenericRealBuild);
        Assert.Equal(0, sessionManager.ExecuteWithApplicationAttempts);
    }

    [Fact]
    public async Task RealSolidWorksWorkerBuildsPlateBasicFourHolesWhenAllSafetySwitchesAreEnabled()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"plate-basic-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var templatePath = Path.Combine(outputRoot, "plate_template.prtdot");
            await File.WriteAllTextAsync(templatePath, "test template marker");
            var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
            var builder = new TestSolidWorksPlateBuilder();
            var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
            {
                ["SW_ENABLE_REAL_EXECUTION"] = "true",
                ["SW_VISIBLE"] = "false",
                ["SW_TEMPLATE_PART_PATH"] = templatePath,
                ["SW_OUTPUT_DIRECTORY"] = outputRoot,
                ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
            });
            var worker = new RealSolidWorksWorker(sessionManager, options, builder);
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                outputRoot,
                DryRun: false,
                AllowRealCadExecution: true);

            var result = await worker.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal(1, sessionManager.ConnectAttempts);
            Assert.Equal(1, sessionManager.ExecuteWithApplicationAttempts);
            Assert.Equal(1, builder.BuildAttempts);
            Assert.Equal("Completed", result.Status);
            Assert.Equal("RealBuildPlateBasic4Holes", result.ExecutionMode);
            Assert.True(result.RealCadConnected);
            Assert.True(result.RealCadExecuted);
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase));
            Assert.All(result.GeneratedArtifacts, artifact => Assert.True(File.Exists(artifact.FilePath)));
            var reportPath = result.GeneratedArtifacts.Single(artifact => artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase)).FilePath;
            using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            Assert.Equal("RealBuildPlateBasic4Holes", document.RootElement.GetProperty("execution_mode").GetString());
            Assert.True(document.RootElement.GetProperty("real_cad_executed").GetBoolean());
            Assert.True(document.RootElement.TryGetProperty("plane_selection_attempted", out _));
            Assert.True(document.RootElement.TryGetProperty("plane_selection_success", out _));
            Assert.True(document.RootElement.TryGetProperty("selected_plane_name", out _));
            Assert.True(document.RootElement.TryGetProperty("selected_plane_strategy", out _));
            Assert.True(document.RootElement.TryGetProperty("available_reference_planes", out _));
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
    public async Task RealSolidWorksWorkerCreatesBasicViewsDrawingWhenAllSafetySwitchesAreEnabled()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"drawing-basic-views-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var sourcePartPath = Path.Combine(outputRoot, "plate_basic_4holes.SLDPRT");
            await File.WriteAllTextAsync(sourcePartPath, "fake source part bytes for drawing tests");
            var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
            var drawingBuilder = new TestSolidWorksDrawingBuilder();
            var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
            {
                ["SW_ENABLE_REAL_EXECUTION"] = "true",
                ["SW_VISIBLE"] = "false",
                ["SW_OUTPUT_DIRECTORY"] = outputRoot,
                ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
            });
            var worker = new RealSolidWorksWorker(sessionManager, options, new TestSolidWorksPlateBuilder(), drawingBuilder);
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                outputRoot,
                DryRun: false,
                AllowRealCadExecution: true,
                DrawingSmokeTestOnly: true,
                SourcePartPath: sourcePartPath);

            var result = await worker.ExecuteAsync(request, CancellationToken.None);
            var artifactReport = new SolidWorksArtifactValidator(Path.Combine(FindProjectRoot(), "output", "solidworks")).Validate(result);

            Assert.Equal(1, sessionManager.ConnectAttempts);
            Assert.Equal(1, sessionManager.ExecuteWithApplicationAttempts);
            Assert.Equal(1, drawingBuilder.BuildAttempts);
            Assert.Equal("Completed", result.Status);
            Assert.Equal("RealDrawingBasicViews", result.ExecutionMode);
            Assert.True(result.RealCadConnected);
            Assert.True(result.RealCadExecuted);
            Assert.True(artifactReport.IsPassed);
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("drawing_report.json", StringComparison.OrdinalIgnoreCase));
            Assert.All(result.GeneratedArtifacts, artifact => Assert.True(File.Exists(artifact.FilePath)));
            var reportPath = result.GeneratedArtifacts.Single(artifact => artifact.FilePath.EndsWith("drawing_report.json", StringComparison.OrdinalIgnoreCase)).FilePath;
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            Assert.Equal("Passed", document.RootElement.GetProperty("final_status").GetString());
            Assert.True(document.RootElement.GetProperty("drawing_created").GetBoolean());
            Assert.Contains(document.RootElement.GetProperty("views_created").EnumerateArray(), view => view.GetString() == "Front");
            Assert.Contains(document.RootElement.GetProperty("views_created").EnumerateArray(), view => view.GetString() == "Isometric");
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
    public async Task RealSolidWorksWorkerRejectsDrawingWhenSafetySwitchesAreMissing()
    {
        var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "false",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
        });
        var worker = new RealSolidWorksWorker(sessionManager, options, new TestSolidWorksPlateBuilder(), new TestSolidWorksDrawingBuilder());
        var request = new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            await CreatePlanAsync(),
            Path.Combine(Path.GetTempPath(), "solidworks_drawing_reject", Guid.NewGuid().ToString("N")),
            DryRun: false,
            AllowRealCadExecution: true,
            DrawingSmokeTestOnly: true,
            SourcePartPath: Path.Combine(Path.GetTempPath(), "plate_basic_4holes.SLDPRT"));

        var result = await worker.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("Rejected", result.Status);
        Assert.Equal("RealPreflightOnly", result.ExecutionMode);
        Assert.False(result.RealCadConnected);
        Assert.False(result.RealCadExecuted);
        Assert.Equal(0, sessionManager.ConnectAttempts);
        Assert.Contains(result.Issues, issue => issue.Contains("missing_user_safety_confirmation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RealSolidWorksWorkerCreatesDrawingDimensionsWhenAllSafetySwitchesAreEnabled()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"drawing-dimensions-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var sourceDrawingPath = Path.Combine(outputRoot, "plate_basic_4holes.SLDDRW");
            await File.WriteAllTextAsync(sourceDrawingPath, "fake source drawing bytes for dimension tests");
            var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
            var dimensionBuilder = new TestSolidWorksDrawingDimensionBuilder();
            var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
            {
                ["SW_ENABLE_REAL_EXECUTION"] = "true",
                ["SW_VISIBLE"] = "false",
                ["SW_OUTPUT_DIRECTORY"] = outputRoot,
                ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
            });
            var worker = new RealSolidWorksWorker(
                sessionManager,
                options,
                new TestSolidWorksPlateBuilder(),
                new TestSolidWorksDrawingBuilder(),
                dimensionBuilder);
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                outputRoot,
                DryRun: false,
                AllowRealCadExecution: true,
                DrawingDimensionSmokeTestOnly: true,
                SourceDrawingPath: sourceDrawingPath);

            var result = await worker.ExecuteAsync(request, CancellationToken.None);
            var artifactReport = new SolidWorksArtifactValidator(Path.Combine(FindProjectRoot(), "output", "solidworks")).Validate(result);

            Assert.Equal(1, sessionManager.ConnectAttempts);
            Assert.Equal(1, sessionManager.ExecuteWithApplicationAttempts);
            Assert.Equal(1, dimensionBuilder.BuildAttempts);
            Assert.Equal("Completed", result.Status);
            Assert.Equal("RealDrawingDimensions", result.ExecutionMode);
            Assert.True(result.RealCadConnected);
            Assert.True(result.RealCadExecuted);
            Assert.True(artifactReport.IsPassed);
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("plate_basic_4holes_dimensioned.SLDDRW", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("plate_basic_4holes_dimensioned.pdf", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("dimension_report.json", StringComparison.OrdinalIgnoreCase));
            var reportPath = result.GeneratedArtifacts.Single(artifact => artifact.FilePath.EndsWith("dimension_report.json", StringComparison.OrdinalIgnoreCase)).FilePath;
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            Assert.Equal("Passed", document.RootElement.GetProperty("final_status").GetString());
            Assert.True(document.RootElement.GetProperty("drawing_opened").GetBoolean());
            Assert.True(document.RootElement.GetProperty("length_dimension_added").GetBoolean());
            Assert.True(document.RootElement.GetProperty("hole_position_dimension_added").GetBoolean());
            Assert.Contains(document.RootElement.GetProperty("views_confirmed").EnumerateArray(), view => view.GetString() == "Front");
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
    public async Task RealSolidWorksWorkerRejectsDrawingDimensionsWhenSafetySwitchesAreMissing()
    {
        var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "false",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
        });
        var worker = new RealSolidWorksWorker(
            sessionManager,
            options,
            new TestSolidWorksPlateBuilder(),
            new TestSolidWorksDrawingBuilder(),
            new TestSolidWorksDrawingDimensionBuilder());
        var request = new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            await CreatePlanAsync(),
            Path.Combine(Path.GetTempPath(), "solidworks_drawing_dimension_reject", Guid.NewGuid().ToString("N")),
            DryRun: false,
            AllowRealCadExecution: true,
            DrawingDimensionSmokeTestOnly: true,
            SourceDrawingPath: Path.Combine(Path.GetTempPath(), "plate_basic_4holes.SLDDRW"));

        var result = await worker.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("Rejected", result.Status);
        Assert.Equal("RealPreflightOnly", result.ExecutionMode);
        Assert.False(result.RealCadConnected);
        Assert.False(result.RealCadExecuted);
        Assert.Equal(0, sessionManager.ConnectAttempts);
        Assert.Contains(result.Issues, issue => issue.Contains("missing_user_safety_confirmation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RealSolidWorksWorkerCreatesDrawingTitleBlockWhenAllSafetySwitchesAreEnabled()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"drawing-title-block-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var sourceDrawingPath = Path.Combine(outputRoot, "plate_basic_4holes_dimensioned.SLDDRW");
            await File.WriteAllTextAsync(sourceDrawingPath, "fake dimensioned source drawing bytes for title block tests");
            var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
            var titleBlockBuilder = new TestSolidWorksDrawingTitleBlockBuilder();
            var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
            {
                ["SW_ENABLE_REAL_EXECUTION"] = "true",
                ["SW_VISIBLE"] = "false",
                ["SW_OUTPUT_DIRECTORY"] = outputRoot,
                ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
            });
            var worker = new RealSolidWorksWorker(
                sessionManager,
                options,
                new TestSolidWorksPlateBuilder(),
                new TestSolidWorksDrawingBuilder(),
                new TestSolidWorksDrawingDimensionBuilder(),
                titleBlockBuilder);
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                outputRoot,
                DryRun: false,
                AllowRealCadExecution: true,
                DrawingTitleBlockSmokeTestOnly: true,
                SourceDimensionedDrawingPath: sourceDrawingPath);

            var result = await worker.ExecuteAsync(request, CancellationToken.None);
            var artifactReport = new SolidWorksArtifactValidator(Path.Combine(FindProjectRoot(), "output", "solidworks")).Validate(result);

            Assert.Equal(1, sessionManager.ConnectAttempts);
            Assert.Equal(1, sessionManager.ExecuteWithApplicationAttempts);
            Assert.Equal(1, titleBlockBuilder.BuildAttempts);
            Assert.Equal("Completed", result.Status);
            Assert.Equal("RealDrawingTitleBlock", result.ExecutionMode);
            Assert.True(result.RealCadConnected);
            Assert.True(result.RealCadExecuted);
            Assert.True(artifactReport.IsPassed);
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("plate_basic_4holes_title_block.SLDDRW", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("plate_basic_4holes_title_block.pdf", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith("title_block_report.json", StringComparison.OrdinalIgnoreCase));
            var reportPath = result.GeneratedArtifacts.Single(artifact => artifact.FilePath.EndsWith("title_block_report.json", StringComparison.OrdinalIgnoreCase)).FilePath;
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            Assert.Equal("Passed", document.RootElement.GetProperty("final_status").GetString());
            Assert.True(document.RootElement.GetProperty("drawing_opened").GetBoolean());
            Assert.True(document.RootElement.GetProperty("custom_properties_written").GetBoolean());
            Assert.True(document.RootElement.GetProperty("title_block_updated").GetBoolean());
            Assert.Equal("custom_properties_only", document.RootElement.GetProperty("title_block_population_strategy").GetString());
            Assert.False(document.RootElement.GetProperty("title_block_fields_verified_in_sheet_format").GetBoolean());
            Assert.Equal("plate_basic_4holes", document.RootElement.GetProperty("part_name").GetString());
            Assert.Equal("PLATE-BASIC-4HOLES", document.RootElement.GetProperty("drawing_number").GetString());
            Assert.Equal("A", document.RootElement.GetProperty("revision").GetString());
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
    public async Task RealSolidWorksWorkerRejectsDrawingTitleBlockWhenSafetySwitchesAreMissing()
    {
        var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "false",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
        });
        var worker = new RealSolidWorksWorker(
            sessionManager,
            options,
            new TestSolidWorksPlateBuilder(),
            new TestSolidWorksDrawingBuilder(),
            new TestSolidWorksDrawingDimensionBuilder(),
            new TestSolidWorksDrawingTitleBlockBuilder());
        var request = new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            await CreatePlanAsync(),
            Path.Combine(Path.GetTempPath(), "solidworks_drawing_title_block_reject", Guid.NewGuid().ToString("N")),
            DryRun: false,
            AllowRealCadExecution: true,
            DrawingTitleBlockSmokeTestOnly: true,
            SourceDimensionedDrawingPath: Path.Combine(Path.GetTempPath(), "plate_basic_4holes_dimensioned.SLDDRW"));

        var result = await worker.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("Rejected", result.Status);
        Assert.Equal("RealPreflightOnly", result.ExecutionMode);
        Assert.False(result.RealCadConnected);
        Assert.False(result.RealCadExecuted);
        Assert.Equal(0, sessionManager.ConnectAttempts);
        Assert.Contains(result.Issues, issue => issue.Contains("missing_user_safety_confirmation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SolidWorksDrawingReportSchemaSerializesExpectedFields()
    {
        var report = new SolidWorksDrawingReport
        {
            SourcePartPath = @"C:\temp\plate_basic_4holes.SLDPRT",
            OutputDirectory = @"C:\temp\drawing",
            SolidWorksConnected = true,
            SolidWorksVersion = "TestVersion",
            DrawingCreated = true,
            SlddrwPath = @"C:\temp\drawing\plate_basic_4holes.SLDDRW",
            SlddrwExists = true,
            SlddrwSizeBytes = 12,
            PdfPath = @"C:\temp\drawing\plate_basic_4holes.pdf",
            PdfExists = true,
            PdfSizeBytes = 10,
            FinalStatus = "Passed"
        };
        report.ViewsCreated.AddRange(new[] { "Front", "Top", "Right", "Isometric" });
        report.Operations.Add("front_view_create_success");

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.Contains("source_part_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("views_created", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("slddrw_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pdf_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("final_status", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolidWorksDrawingDimensionReportSchemaSerializesExpectedFields()
    {
        var report = new SolidWorksDrawingDimensionReport
        {
            SourceDrawingPath = @"C:\temp\plate_basic_4holes.SLDDRW",
            OutputDirectory = @"C:\temp\drawing_dimensions",
            SolidWorksConnected = true,
            SolidWorksVersion = "TestVersion",
            DrawingOpened = true,
            LengthDimensionAdded = true,
            WidthDimensionAdded = true,
            ThicknessDimensionAdded = true,
            HoleDiameterDimensionAdded = true,
            HolePositionDimensionAdded = true,
            SlddrwPath = @"C:\temp\drawing_dimensions\plate_basic_4holes_dimensioned.SLDDRW",
            SlddrwExists = true,
            SlddrwSizeBytes = 12,
            PdfPath = @"C:\temp\drawing_dimensions\plate_basic_4holes_dimensioned.pdf",
            PdfExists = true,
            PdfSizeBytes = 10,
            FinalStatus = "Passed"
        };
        report.ViewsConfirmed.AddRange(new[] { "Front", "Top", "Right", "Isometric" });
        report.Dimensions.Add(new SolidWorksDrawingDimensionResult(
            "plate_length",
            160,
            "Passed",
            "length_dimension_failed",
            "IDrawingDoc.CreateLinearDim4_non_associative",
            "CreateLinearDim4 returned a display dimension."));

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.Contains("source_drawing_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("views_confirmed", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length_dimension_added", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dimensions", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("final_status", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolidWorksDrawingTitleBlockReportSchemaSerializesExpectedFields()
    {
        var report = new SolidWorksDrawingTitleBlockReport
        {
            SourceDimensionedDrawingPath = @"C:\temp\plate_basic_4holes_dimensioned.SLDDRW",
            OutputDirectory = @"C:\temp\title_block",
            SolidWorksConnected = true,
            SolidWorksVersion = "TestVersion",
            DrawingOpened = true,
            TitleBlockTemplateDetected = true,
            DrawingPropertiesRead = true,
            CustomPropertiesWritten = true,
            TitleBlockUpdated = true,
            PartName = "plate_basic_4holes",
            DrawingNumber = "PLATE-BASIC-4HOLES",
            Material = "Q235",
            Scale = "1:1",
            DrawingDate = "2026-07-06",
            Revision = "A",
            SlddrwPath = @"C:\temp\title_block\plate_basic_4holes_title_block.SLDDRW",
            SlddrwExists = true,
            SlddrwSizeBytes = 12,
            PdfPath = @"C:\temp\title_block\plate_basic_4holes_title_block.pdf",
            PdfExists = true,
            PdfSizeBytes = 10,
            FinalStatus = "Passed"
        };
        report.Properties.Add(new SolidWorksDrawingTitleBlockProperty(
            "PartName",
            "plate_basic_4holes",
            "Passed",
            "custom_property_write_failed",
            "IModelDocExtension.CustomPropertyManager_Add3_Set2_Get6",
            "Custom property written."));

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.Contains("source_dimensioned_drawing_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title_block_template_detected", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("custom_properties_written", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("part_name", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drawing_number", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title_block_population_strategy", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title_block_fields_verified_in_sheet_format", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("custom_properties_only", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("properties", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("final_status", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolidWorksReleasePackageSchemasSerializeExpectedFields()
    {
        var manifest = new SolidWorksReleaseManifest
        {
            SourceRoot = @"C:\temp\project",
            OutputDirectory = @"C:\temp\project\output\solidworks\release\plate_basic_4holes\run",
            FinalStatus = "Passed"
        };
        manifest.Artifacts.Add(new SolidWorksReleaseManifestItem
        {
            Name = "plate_basic_4holes.SLDPRT",
            Kind = "Artifact",
            SourcePath = @"C:\temp\source\plate_basic_4holes.SLDPRT",
            PackagePath = @"C:\temp\project\output\solidworks\release\plate_basic_4holes\run\artifacts\plate_basic_4holes.SLDPRT",
            Exists = true,
            SizeBytes = 10,
            Sha256 = "abc"
        });
        manifest.Reports.Add(new SolidWorksReleaseManifestItem
        {
            Name = "build_report.json",
            Kind = "Report",
            Exists = true,
            SizeBytes = 10,
            FinalStatus = "Passed"
        });
        var quality = new SolidWorksPackageQualityReport
        {
            OutputDirectory = manifest.OutputDirectory,
            ManifestExists = true,
            ReleaseSummaryExists = true,
            ArtifactsCollected = true,
            ReportsCollected = true,
            PdfExists = true,
            PathsUnderReleaseDirectory = true,
            FileSizesValid = true,
            ReportsFinalStatusChecked = true,
            FailureStagesChecked = true,
            PackageBuildStatus = "Passed",
            SourceReportsChecked = true,
            AllSourceReportsPassed = true,
            DeliverableStatus = "Deliverable",
            FinalStatus = "Passed"
        };
        quality.Checks.Add(new SolidWorksPackageQualityCheck("artifacts_collected", "Passed", null, "ok"));

        var json = JsonSerializer.Serialize(new { manifest, quality }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.Contains("release_id", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("artifacts", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reports", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quality_report_id", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reports_final_status_checked", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("failure_stages_checked", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package_build_status", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("all_source_reports_passed", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_reports_checked", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_report_failures", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_report_warnings", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deliverable_status", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SolidWorksReleasePackageBuilderCollectsControlledSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_release_package_success", Guid.NewGuid().ToString("N"));
        try
        {
            await WriteReleasePackageFixtureAsync(root);
            var releaseRoot = Path.Combine(root, "output", "solidworks", "release", "plate_basic_4holes");

            var result = await new SolidWorksReleasePackageBuilder().BuildAsync(root, releaseRoot);

            Assert.Equal("Completed", result.Status);
            Assert.Null(result.FailureStage);
            Assert.True(result.ArtifactsCollected);
            Assert.True(result.ReportsCollected);
            Assert.True(File.Exists(result.ManifestPath));
            Assert.True(File.Exists(result.QualityReportPath));
            Assert.True(File.Exists(result.SummaryPath));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "artifacts", "plate_basic_4holes.SLDPRT")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "artifacts", "plate_basic_4holes.STEP")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "artifacts", "plate_basic_4holes.SLDDRW")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "artifacts", "plate_basic_4holes.pdf")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "reports", "build_report.json")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "reports", "diagnostic_report.json")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "reports", "drawing_report.json")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "reports", "dimension_report.json")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "reports", "title_block_report.json")));

            using var qualityJson = JsonDocument.Parse(await File.ReadAllTextAsync(result.QualityReportPath!));
            Assert.Equal("Passed", qualityJson.RootElement.GetProperty("final_status").GetString());
            Assert.Equal("Passed", qualityJson.RootElement.GetProperty("package_build_status").GetString());
            Assert.True(qualityJson.RootElement.GetProperty("all_source_reports_passed").GetBoolean());
            Assert.Equal("Deliverable", qualityJson.RootElement.GetProperty("deliverable_status").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SolidWorksReleasePackageBuilderBlocksDeliverableWhenSourceReportFailed()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_release_package_failed_source", Guid.NewGuid().ToString("N"));
        try
        {
            await WriteReleasePackageFixtureAsync(root, failedReportName: "dimension_report.json");
            var releaseRoot = Path.Combine(root, "output", "solidworks", "release", "plate_basic_4holes");

            var result = await new SolidWorksReleasePackageBuilder().BuildAsync(root, releaseRoot);

            Assert.Equal("Failed", result.Status);
            Assert.Equal("source_report_failed", result.FailureStage);
            Assert.True(result.ArtifactsCollected);
            Assert.True(result.ReportsCollected);

            using var qualityJson = JsonDocument.Parse(await File.ReadAllTextAsync(result.QualityReportPath!));
            var rootElement = qualityJson.RootElement;
            Assert.Equal("Passed", rootElement.GetProperty("package_build_status").GetString());
            Assert.False(rootElement.GetProperty("all_source_reports_passed").GetBoolean());
            Assert.Equal("NotDeliverable", rootElement.GetProperty("deliverable_status").GetString());
            Assert.Equal("Failed", rootElement.GetProperty("final_status").GetString());
            Assert.Contains(rootElement.GetProperty("source_report_failures").EnumerateArray(), failure =>
                failure.GetProperty("name").GetString() == "dimension_report.json" &&
                failure.GetProperty("final_status").GetString() == "Failed");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SolidWorksReleasePackageBuilderReportsMissingSourcesWithActionableFailureStage()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_release_package_missing", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var releaseRoot = Path.Combine(root, "output", "solidworks", "release", "plate_basic_4holes");

            var result = await new SolidWorksReleasePackageBuilder().BuildAsync(root, releaseRoot);

            Assert.Equal("Failed", result.Status);
            Assert.Equal("source_artifacts_missing", result.FailureStage);
            Assert.False(result.ArtifactsCollected);
            Assert.False(result.ReportsCollected);
            Assert.True(File.Exists(result.ManifestPath));
            Assert.True(File.Exists(result.QualityReportPath));
            Assert.True(File.Exists(result.SummaryPath));

            using var qualityJson = JsonDocument.Parse(await File.ReadAllTextAsync(result.QualityReportPath!));
            Assert.Equal("Failed", qualityJson.RootElement.GetProperty("final_status").GetString());
            Assert.Equal("source_artifacts_missing", qualityJson.RootElement.GetProperty("failure_stage").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksDrawingBuilderAndSmokeRunnerAreAvailable()
    {
        Assert.NotNull(typeof(LateBoundSolidWorksDrawingBuilder).GetMethod(nameof(LateBoundSolidWorksDrawingBuilder.CreateBasicViewsDrawingAsync)));
        Assert.Equal("front_view_create_failed", LateBoundSolidWorksDrawingBuilder.MapDrawingFailureStage("front_view_create_failed: view null"));
        Assert.Equal("pdf_export_failed", LateBoundSolidWorksDrawingBuilder.MapDrawingFailureStage("pdf file missing"));
        Assert.True(File.Exists(Path.Combine(FindProjectRoot(), "tools", "SolidWorksDrawingSmokeRunner", "SolidWorksDrawingSmokeRunner.csproj")));
        Assert.NotNull(typeof(LateBoundSolidWorksDrawingDimensionBuilder).GetMethod(nameof(LateBoundSolidWorksDrawingDimensionBuilder.CreateDimensionedDrawingAsync)));
        Assert.Equal("length_dimension_failed", LateBoundSolidWorksDrawingDimensionBuilder.MapDrawingDimensionFailureStage("length_dimension_failed: CreateLinearDim4 returned null"));
        Assert.Equal("drawing_dimension_api_evidence_insufficient", LateBoundSolidWorksDrawingDimensionBuilder.MapDrawingDimensionFailureStage("unknown dimension api"));
        Assert.True(File.Exists(Path.Combine(FindProjectRoot(), "tools", "SolidWorksDrawingDimensionSmokeRunner", "SolidWorksDrawingDimensionSmokeRunner.csproj")));
        Assert.NotNull(typeof(LateBoundSolidWorksDrawingTitleBlockBuilder).GetMethod(nameof(LateBoundSolidWorksDrawingTitleBlockBuilder.ApplyTitleBlockAsync)));
        Assert.Equal("custom_property_write_failed", LateBoundSolidWorksDrawingTitleBlockBuilder.MapDrawingTitleBlockFailureStage("CustomPropertyManager Add3 failed"));
        Assert.Equal("drawing_title_block_api_evidence_insufficient", LateBoundSolidWorksDrawingTitleBlockBuilder.MapDrawingTitleBlockFailureStage("unknown title block api"));
        Assert.True(File.Exists(Path.Combine(FindProjectRoot(), "tools", "SolidWorksDrawingTitleBlockSmokeRunner", "SolidWorksDrawingTitleBlockSmokeRunner.csproj")));
        Assert.NotNull(typeof(SolidWorksReleasePackageBuilder).GetMethod(nameof(SolidWorksReleasePackageBuilder.BuildAsync)));
        Assert.NotNull(typeof(SolidWorksReleasePackageValidator).GetMethod(nameof(SolidWorksReleasePackageValidator.Validate)));
    }

    [Fact]
    public void SolidWorksDrawingTitleBlockBuilderWritePropertyFailsWhenReadBackDiffers()
    {
        var writeProperty = typeof(LateBoundSolidWorksDrawingTitleBlockBuilder).GetMethod(
            "WriteProperty",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(writeProperty);

        var property = Assert.IsType<SolidWorksDrawingTitleBlockProperty>(writeProperty.Invoke(
            null,
            new object[]
            {
                new FakeTitleBlockCustomPropertyManager("wrong-part-name"),
                "PartName",
                "plate_basic_4holes"
            }));

        Assert.Equal("Failed", property.Status);
        Assert.Equal("custom_property_write_failed", property.FailureStage);
        Assert.Contains("wrong-part-name", property.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolidWorksDrawingBuilderSaveDrawingRejectsEmptySlddrwFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_drawing_empty_save_test", Guid.NewGuid().ToString("N"));
        var drawingPath = Path.Combine(root, "plate_basic_4holes.SLDDRW");
        var saveDrawing = typeof(LateBoundSolidWorksDrawingBuilder).GetMethod(
            "SaveDrawing",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(saveDrawing);
        var report = new SolidWorksDrawingReport
        {
            SlddrwPath = drawingPath
        };
        var logs = new List<string>();

        try
        {
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => saveDrawing.Invoke(
                null,
                new object[] { new FakeTitleBlockDrawingDocument(), drawingPath, report, logs }));

            Assert.IsType<IOException>(exception.GetBaseException());
            Assert.Contains("slddrw_save_failed", exception.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(drawingPath));
            Assert.Equal(0, new FileInfo(drawingPath).Length);
            Assert.True(report.SlddrwExists);
            Assert.Equal(0, report.SlddrwSizeBytes);
            Assert.DoesNotContain("slddrw_save_success", report.Operations);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksDrawingDimensionBuilderSaveDrawingRejectsEmptySlddrwFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_dimension_empty_save_test", Guid.NewGuid().ToString("N"));
        var drawingPath = Path.Combine(root, "plate_basic_4holes_dimensioned.SLDDRW");
        var saveDrawing = typeof(LateBoundSolidWorksDrawingDimensionBuilder).GetMethod(
            "SaveDrawing",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(saveDrawing);
        var report = new SolidWorksDrawingDimensionReport
        {
            SlddrwPath = drawingPath
        };
        var logs = new List<string>();

        try
        {
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => saveDrawing.Invoke(
                null,
                new object[] { new FakeTitleBlockDrawingDocument(), drawingPath, report, logs }));

            Assert.IsType<IOException>(exception.GetBaseException());
            Assert.Contains("dimension_save_failed", exception.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(drawingPath));
            Assert.Equal(0, new FileInfo(drawingPath).Length);
            Assert.True(report.SlddrwExists);
            Assert.Equal(0, report.SlddrwSizeBytes);
            Assert.DoesNotContain("dimension_save_success", report.Operations);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksDrawingTitleBlockBuilderSaveDrawingRejectsEmptySlddrwFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_title_block_empty_save_test", Guid.NewGuid().ToString("N"));
        var drawingPath = Path.Combine(root, "plate_basic_4holes_title_block.SLDDRW");
        var saveDrawing = typeof(LateBoundSolidWorksDrawingTitleBlockBuilder).GetMethod(
            "SaveDrawing",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(saveDrawing);
        var report = new SolidWorksDrawingTitleBlockReport
        {
            SlddrwPath = drawingPath
        };
        var logs = new List<string>();

        try
        {
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => saveDrawing.Invoke(
                null,
                new object[] { new FakeTitleBlockDrawingDocument(), drawingPath, report, logs }));

            Assert.IsType<IOException>(exception.GetBaseException());
            Assert.Contains("title_block_save_failed", exception.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(drawingPath));
            Assert.Equal(0, new FileInfo(drawingPath).Length);
            Assert.True(report.SlddrwExists);
            Assert.Equal(0, report.SlddrwSizeBytes);
            Assert.DoesNotContain("title_block_save_success", report.Operations);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RealSolidWorksWorkerDisconnectTimeoutDoesNotHideBuildResult()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"disconnect-timeout-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var templatePath = Path.Combine(outputRoot, "plate_template.prtdot");
            await File.WriteAllTextAsync(templatePath, "test template marker");
            var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true)
            {
                WaitForDisconnectCancellation = true
            };
            var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
            {
                ["SW_ENABLE_REAL_EXECUTION"] = "true",
                ["SW_VISIBLE"] = "false",
                ["SW_TEMPLATE_PART_PATH"] = templatePath,
                ["SW_OUTPUT_DIRECTORY"] = outputRoot,
                ["SW_CONNECT_TIMEOUT_SECONDS"] = "1"
            });
            var worker = new RealSolidWorksWorker(sessionManager, options, new TestSolidWorksPlateBuilder());
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                outputRoot,
                DryRun: false,
                AllowRealCadExecution: true);

            var result = await worker.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal("Completed", result.Status);
            Assert.Equal(1, sessionManager.DisconnectAttempts);
            Assert.True(sessionManager.DisconnectTokenCanBeCanceled);
            Assert.Contains(result.Logs, log => log.Contains("solidworks_disconnect_timeout", StringComparison.OrdinalIgnoreCase));
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
    public async Task RealSolidWorksWorkerReturnsFailedWhenExecutionTimesOut()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"execution-timeout-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var templatePath = Path.Combine(outputRoot, "plate_template.prtdot");
            await File.WriteAllTextAsync(templatePath, "test template marker");
            var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true)
            {
                ThrowExecutionTimeout = true
            };
            var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
            {
                ["SW_ENABLE_REAL_EXECUTION"] = "true",
                ["SW_VISIBLE"] = "false",
                ["SW_TEMPLATE_PART_PATH"] = templatePath,
                ["SW_OUTPUT_DIRECTORY"] = outputRoot,
                ["SW_CONNECT_TIMEOUT_SECONDS"] = "5",
                ["SW_EXECUTION_TIMEOUT_SECONDS"] = "1"
            });
            var worker = new RealSolidWorksWorker(sessionManager, options, new TestSolidWorksPlateBuilder());
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                outputRoot,
                DryRun: false,
                AllowRealCadExecution: true);

            var result = await worker.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal("Failed", result.Status);
            Assert.Equal("RealBuildPlateBasic4Holes", result.ExecutionMode);
            Assert.True(result.RealCadConnected);
            Assert.False(result.RealCadExecuted);
            Assert.Equal(1, sessionManager.ExecuteWithApplicationAttempts);
            Assert.Contains(result.Issues, issue => issue.Contains("solidworks_execution_timeout", StringComparison.OrdinalIgnoreCase));
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
    public async Task RealSolidWorksWorkerRejectsUnsupportedRealBuildPlanBeforeConnecting()
    {
        var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
        var builder = new TestSolidWorksPlateBuilder();
        var worker = new RealSolidWorksWorker(
            sessionManager,
            SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
            {
                ["SW_ENABLE_REAL_EXECUTION"] = "true",
                ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
            }),
            builder);
        var unsupportedPlan = await CreatePlanAsync();
        var request = new SolidWorksWorkerRequest(
            $"request-{Guid.NewGuid():N}",
            unsupportedPlan with { PartType = "unsupported_part" },
            Path.Combine(Path.GetTempPath(), "solidworks_real_worker_unsupported", Guid.NewGuid().ToString("N")),
            DryRun: false,
            AllowRealCadExecution: true);

        var result = await worker.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal("Rejected", result.Status);
        Assert.Equal("RealPreflightOnly", result.ExecutionMode);
        Assert.Contains(result.Issues, issue => issue.Contains("unsupported_real_build_plan", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.RealCadExecuted);
        Assert.False(result.RealCadConnected);
        Assert.Equal(0, sessionManager.ConnectAttempts);
        Assert.Equal(0, builder.BuildAttempts);
    }

    [Fact]
    public async Task RealSolidWorksWorkerWritesBuildReportWhenTemplateIsMissing()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", "plate_basic_4holes", $"template-missing-{Guid.NewGuid():N}");
        try
        {
            var sessionManager = new CountingSolidWorksSessionManager(connectsSuccessfully: true);
            var worker = new RealSolidWorksWorker(
                sessionManager,
                SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
                {
                    ["SW_ENABLE_REAL_EXECUTION"] = "true",
                    ["SW_OUTPUT_DIRECTORY"] = outputRoot,
                    ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
                }));
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                outputRoot,
                DryRun: false,
                AllowRealCadExecution: true);

            var result = await worker.ExecuteAsync(request, CancellationToken.None);

            var reportArtifact = Assert.Single(result.GeneratedArtifacts, artifact =>
                artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Failed", result.Status);
            Assert.Equal("RealBuildPlateBasic4Holes", result.ExecutionMode);
            Assert.False(result.RealCadExecuted);
            Assert.False(result.RealCadConnected);
            Assert.Equal(0, sessionManager.ConnectAttempts);
            Assert.Contains(result.Issues, issue => issue.Contains("template_part_path_required_for_real_build", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(reportArtifact.FilePath));
            using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(reportArtifact.FilePath));
            Assert.Equal("Failed", document.RootElement.GetProperty("final_status").GetString());
            Assert.False(document.RootElement.GetProperty("sldprt_save_attempted").GetBoolean());
            Assert.False(document.RootElement.GetProperty("step_export_attempted").GetBoolean());
            Assert.Contains(
                document.RootElement.GetProperty("operations_executed").EnumerateArray(),
                item => item.GetString() == "preflight_failed");
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
    public async Task SolidWorksArtifactValidatorAcceptsControlledRealPlateArtifacts()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"validator-real-test-{Guid.NewGuid():N}");
        try
        {
            var result = await new TestSolidWorksPlateBuilder().BuildPlateBasicFourHolesAsync(
                new object(),
                new SolidWorksWorkerRequest(
                    $"request-{Guid.NewGuid():N}",
                    await CreatePlanAsync(),
                    outputRoot,
                    DryRun: false,
                    AllowRealCadExecution: true),
                SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
                {
                    ["SW_ENABLE_REAL_EXECUTION"] = "true",
                    ["SW_OUTPUT_DIRECTORY"] = outputRoot
                }),
                "TestVersion",
                CancellationToken.None);

            var workerResult = new SolidWorksWorkerResult(
                $"request-{Guid.NewGuid():N}",
                "Completed",
                result.GeneratedArtifacts,
                result.Logs,
                result.Issues,
                "RealBuildPlateBasic4Holes",
                RealCadExecuted: true,
                RealCadConnected: true);

            var report = new SolidWorksArtifactValidator(Path.Combine(FindProjectRoot(), "output", "solidworks")).Validate(workerResult);

            Assert.True(report.IsPassed);
            Assert.Empty(report.Issues);
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
    public async Task SolidWorksArtifactValidatorRejectsMissingStepArtifact()
    {
        var outputRoot = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", "plate_basic_4holes", $"missing-step-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var partPath = Path.Combine(outputRoot, "plate_basic_4holes.SLDPRT");
            var reportPath = Path.Combine(outputRoot, "build_report.json");
            await File.WriteAllTextAsync(partPath, "part bytes");
            await SolidWorksPlateBuildReportWriter.WriteAsync(
                reportPath,
                new SolidWorksWorkerRequest(
                    $"request-{Guid.NewGuid():N}",
                    await CreatePlanAsync(),
                    outputRoot,
                    DryRun: false,
                    AllowRealCadExecution: true),
                "RealBuildPlateBasic4Holes",
                realCadExecuted: true,
                realCadConnected: true,
                "TestVersion",
                outputRoot,
                new[] { partPath },
                new SolidWorksPlateBuildDiagnostics
                {
                    SldprtSaveAttempted = true,
                    SldprtSaveSuccess = true,
                    SldprtPath = partPath,
                    SldprtSizeBytes = new FileInfo(partPath).Length,
                    StepExportAttempted = true,
                    StepExportSuccess = false,
                    StepPath = Path.Combine(outputRoot, "plate_basic_4holes.STEP")
                },
                "Failed",
                CancellationToken.None);

            var workerResult = new SolidWorksWorkerResult(
                $"request-{Guid.NewGuid():N}",
                "Failed",
                new[]
                {
                    SolidWorksPlateBuildOutput.Artifact("real-part", "Part", partPath, ".SLDPRT", "Part."),
                    SolidWorksPlateBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "Report.")
                },
                Array.Empty<string>(),
                new[] { "step_export_failed" },
                "RealBuildPlateBasic4Holes",
                RealCadExecuted: true,
                RealCadConnected: true);

            var report = new SolidWorksArtifactValidator(Path.Combine(FindProjectRoot(), "output", "solidworks")).Validate(workerResult);

            Assert.False(report.IsPassed);
            Assert.Contains(report.Issues, issue => issue.Contains(".STEP artifact", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(report.Issues, issue => issue.Contains("step_export_success", StringComparison.OrdinalIgnoreCase));
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
    public async Task SolidWorksArtifactValidatorRejectsRealArtifactsOutsideConfiguredOutputRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_real_artifact_path_test", Guid.NewGuid().ToString("N"));
        var allowedRoot = Path.Combine(root, "output", "solidworks");
        var outsideOutputDirectory = Path.Combine(root, "other", "output", "solidworks", "real", "plate_basic_4holes");

        try
        {
            Directory.CreateDirectory(outsideOutputDirectory);
            var partPath = Path.Combine(outsideOutputDirectory, "plate_basic_4holes.SLDPRT");
            var stepPath = Path.Combine(outsideOutputDirectory, "plate_basic_4holes.STEP");
            var reportPath = Path.Combine(outsideOutputDirectory, "build_report.json");
            await File.WriteAllTextAsync(partPath, "outside configured real part");
            await File.WriteAllTextAsync(stepPath, "outside configured step");
            await File.WriteAllTextAsync(
                reportPath,
                "{\"real_cad_executed\":true,\"execution_mode\":\"RealBuildPlateBasic4Holes\"}");

            var workerResult = new SolidWorksWorkerResult(
                $"request-{Guid.NewGuid():N}",
                "Completed",
                new[]
                {
                    SolidWorksPlateBuildOutput.Artifact("real-part", "Part", partPath, ".SLDPRT", "Outside configured real part."),
                    SolidWorksPlateBuildOutput.Artifact("real-step", "Step", stepPath, ".STEP", "Outside configured STEP."),
                    SolidWorksPlateBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "Outside configured report.")
                },
                Array.Empty<string>(),
                Array.Empty<string>(),
                "RealBuildPlateBasic4Holes",
                RealCadExecuted: true,
                RealCadConnected: true);

            var report = new SolidWorksArtifactValidator(allowedRoot).Validate(workerResult);

            Assert.False(report.IsPassed);
            Assert.Contains(report.Issues, issue => issue.Contains("output/solidworks/real", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SolidWorksBuildPlanReviewerRejectsUnsafePlateHoleDiameter()
    {
        var plan = await CreatePlanAsync();
        var cut = plan.Operations.Single(operation => operation.OperationType == "CutExtrude");
        var invalidCut = cut with
        {
            Parameters = new Dictionary<string, string>(cut.Parameters)
            {
                ["hole_diameter_mm"] = "80"
            }
        };

        var report = new SolidWorksBuildPlanReviewer().Review(plan with
        {
            Operations = plan.Operations.Select(operation => operation.OperationId == cut.OperationId ? invalidCut : operation).ToArray()
        });

        Assert.False(report.IsPassed);
        Assert.Contains(report.Issues, issue => issue.Contains("hole diameter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SolidWorksSessionManagerReleasesCreatedApplicationWhenConnectionTimesOut()
    {
        var comActivator = new TrackingSolidWorksComActivator
        {
            SetVisibleAction = (_, _, cancellationToken) =>
            {
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        var manager = new SolidWorksSessionManager(comActivator);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "true",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "1"
        });

        var result = await manager.ConnectAsync(options, CancellationToken.None);

        Assert.False(result.Connected);
        Assert.Contains(result.Issues, issue => issue.Contains("timeout", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, comActivator.CreatedApplications);
        Assert.Equal(1, comActivator.ReleasedApplications);
    }

    [Fact]
    public async Task SolidWorksSessionManagerReturnsTimeoutWhenComCallIgnoresCancellation()
    {
        var comActivator = new TrackingSolidWorksComActivator
        {
            SetVisibleAction = (_, _, _) => Thread.Sleep(TimeSpan.FromSeconds(3))
        };
        var manager = new SolidWorksSessionManager(comActivator);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "true",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "1"
        });
        var started = DateTimeOffset.UtcNow;

        var result = await manager.ConnectAsync(options, CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.False(result.Connected);
        Assert.True(elapsed < TimeSpan.FromSeconds(2.5), $"Non-cooperative COM call should time out before it returns; elapsed {elapsed}.");
        Assert.Contains(result.Issues, issue => issue.Contains("timeout", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, comActivator.CreatedApplications);
        await WaitForAsync(() => comActivator.ReleasedApplications == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, comActivator.ReleasedApplications);
    }

    [Fact]
    public async Task SolidWorksSessionManagerReturnsTimeoutWhenExecutionIgnoresCancellation()
    {
        var comActivator = new TrackingSolidWorksComActivator();
        var manager = new SolidWorksSessionManager(comActivator);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "true",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "5",
            ["SW_EXECUTION_TIMEOUT_SECONDS"] = "1"
        });
        var connection = await manager.ConnectAsync(options, CancellationToken.None);
        Assert.True(connection.Connected);
        var started = DateTimeOffset.UtcNow;

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => manager.ExecuteWithApplicationAsync(
            (_, _) =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(3));
                return Task.FromResult("completed-too-late");
            },
            CancellationToken.None,
            options.ExecutionTimeoutSeconds));
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Contains("solidworks_execution_timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(elapsed < TimeSpan.FromSeconds(2.5), $"Non-cooperative COM execution should time out before it returns; elapsed {elapsed}.");
        Assert.Equal(1, comActivator.CreatedApplications);
        Assert.Equal(1, comActivator.ReleasedApplications);
    }

    [Fact]
    public async Task SolidWorksSessionManagerSerializesConcurrentConnections()
    {
        var comActivator = new TrackingSolidWorksComActivator
        {
            SetVisibleAction = (_, _, cancellationToken) =>
            {
                Thread.Sleep(75);
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        var manager = new SolidWorksSessionManager(comActivator);
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "true",
            ["SW_CONNECT_TIMEOUT_SECONDS"] = "5"
        });

        var first = manager.ConnectAsync(options, CancellationToken.None);
        var second = manager.ConnectAsync(options, CancellationToken.None);
        var results = await Task.WhenAll(first, second);
        await manager.DisconnectAsync(CancellationToken.None);

        Assert.All(results, result => Assert.True(result.Connected));
        Assert.Equal(2, comActivator.CreatedApplications);
        Assert.True(comActivator.MaxConcurrentApplications <= 1);
        Assert.Equal(2, comActivator.ReleasedApplications);
    }

    [Fact]
    public void SolidWorksSessionConnectionResultDoesNotExposeRawComApplication()
    {
        Assert.Null(typeof(SolidWorksSessionConnectionResult).GetProperty("Application"));
    }

    [Fact]
    public async Task SolidWorksPlateBuildOutputAddsUniqueSuffixWhenTargetAlreadyExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_output_collision_test", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "plate_basic_4holes");

        try
        {
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "existing.txt"), "existing output marker");
            var request = new SolidWorksWorkerRequest(
                $"request-{Guid.NewGuid():N}",
                await CreatePlanAsync(),
                root,
                DryRun: false,
                AllowRealCadExecution: true);
            var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
            {
                ["SW_OUTPUT_DIRECTORY"] = root
            });

            var outputDirectories = Enumerable.Range(0, 8)
                .Select(_ => SolidWorksPlateBuildOutput.ResolveOutputDirectory(request, options))
                .ToArray();

            Assert.Equal(outputDirectories.Length, outputDirectories.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(outputDirectories, directory =>
            {
                Assert.Equal("plate_basic_4holes", Path.GetFileName(Path.GetDirectoryName(directory)));
                Assert.Matches(
                    "^[0-9]{8}_[0-9]{6}_[0-9]{3}_[0-9a-f]{32}$",
                    Path.GetFileName(directory));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksRuntimeOptionsDefaultOutputDirectoryIsAbsolute()
    {
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>());

        Assert.True(Path.IsPathFullyQualified(options.OutputDirectory));
        Assert.EndsWith(Path.Combine("output", "solidworks", "real"), options.OutputDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolidWorksRuntimeOptionsParsesExecutionTimeout()
    {
        var options = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_EXECUTION_TIMEOUT_SECONDS"] = "7"
        });
        var invalid = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_EXECUTION_TIMEOUT_SECONDS"] = "invalid"
        });
        var clamped = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["SW_EXECUTION_TIMEOUT_SECONDS"] = "999999"
        });

        Assert.Equal(7, options.ExecutionTimeoutSeconds);
        Assert.Equal(SolidWorksRuntimeOptions.DefaultExecutionTimeoutSeconds, invalid.ExecutionTimeoutSeconds);
        Assert.Equal(SolidWorksRuntimeOptions.MaximumExecutionTimeoutSeconds, clamped.ExecutionTimeoutSeconds);
    }

    [Fact]
    public async Task GatewayAndAgentRegistryDoNotExposeRealSolidWorksWorker()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var dispatcher = new AgentMessageDispatcher(platform);

        var response = await dispatcher.DispatchAsync(
            "RealSolidWorksWorker",
            new GatewayMessageRequest(
                "test",
                "channel",
                "conversation",
                "user",
                "Try to directly call RealSolidWorksWorker",
                Array.Empty<string>(),
                new Dictionary<string, string>()));

        Assert.Null(response);
        Assert.Null(platform.AgentRegistry.GetById("RealSolidWorksWorker"));
        Assert.DoesNotContain(platform.AgentRegistry.GetPublicAgents(), agent => agent.Id.Contains("solidworks", StringComparison.OrdinalIgnoreCase));
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
            Assert.Null(report.SolidWorksSelfCheckError);
            Assert.True(report.SolidWorksRealWorkerSkeletonExists);
            Assert.True(report.SolidWorksEnvironmentValidatorExists);
            Assert.True(report.SolidWorksPreflightReportGenerated);
            Assert.True(report.SolidWorksSessionManagerExists);
            Assert.True(report.SolidWorksRealExecutionDefaultDisabled);
            Assert.True(report.SolidWorksRealExecutionRequiresRequestFlag);
            Assert.True(report.SolidWorksRealExecutionRequiresEnvFlag);
            Assert.True(report.SolidWorksComNotCalledInDefaultSelfCheck);
            Assert.False(report.SolidWorksRealConnectionSmokeTestAttempted);
            Assert.False(report.SolidWorksRealConnectionSmokeTestPassed);
            Assert.Null(report.SolidWorksRealConnectionSmokeTestError);
            Assert.True(report.SolidWorksGenericRealBuildNotImplemented);
            Assert.True(report.SolidWorksRealCadNotExecutedByDefault);
            Assert.True(report.SolidWorksRealPlateBuildImplemented);
            Assert.True(report.SolidWorksRealBuildRequiresEnvFlag);
            Assert.True(report.SolidWorksRealBuildRequiresRequestFlag);
            Assert.True(report.SolidWorksRealBuildRequiresDryRunFalse);
            Assert.True(report.SolidWorksRealBuildDefaultDisabled);
            Assert.False(report.SolidWorksRealBuildSmokeTestAttempted);
            Assert.False(report.SolidWorksRealBuildSmokeTestPassed);
            Assert.Null(report.SolidWorksRealBuildSmokeTestError);
            Assert.False(report.SolidWorksRealBuildArtifactsValidated);
            Assert.False(report.SolidWorksRealBuildReportGenerated);
            Assert.False(report.SolidWorksRealBuildOutputsSldprt);
            Assert.False(report.SolidWorksRealBuildOutputsStep);
            Assert.False(report.SolidWorksRealBuildOutputsJsonReport);
            Assert.True(report.SolidWorksRealBuildNotCalledInDefaultSelfCheck);
            Assert.True(report.SolidWorksDiagnosticRunnerExists);
            Assert.True(report.SolidWorksDiagnosticRunnerNotCalledByDefault);
            if (report.SolidWorksLatestDiagnosticReportPath is not null)
            {
                Assert.True(File.Exists(report.SolidWorksLatestDiagnosticReportPath));
                using var diagnosticReport = JsonDocument.Parse(await File.ReadAllTextAsync(report.SolidWorksLatestDiagnosticReportPath));
                Assert.Equal(
                    diagnosticReport.RootElement.GetProperty("final_status").GetString(),
                    report.SolidWorksLatestDiagnosticFinalStatus);
            }
            else
            {
                Assert.Null(report.SolidWorksLatestDiagnosticFinalStatus);
            }
            Assert.Null(report.SolidWorksRealBuildFailureStage);
            Assert.True(report.SolidWorksRealBuildErrorIsActionable);
            Assert.True(report.SolidWorksApiFailureAnalyzerExists);
            Assert.True(report.SolidWorksApiEvidenceCollectorExists);
            Assert.True(report.SolidWorksApiEvidenceReportSchemaExists);
            Assert.True(report.SolidWorksCutHolesApiEvidenceSupported);
            Assert.True(report.SolidWorksReferenceSkillReadonlyAnalysisSupported);
            Assert.True(report.SolidWorksExternalScriptsNotCopied);
            Assert.True(report.SolidWorksApiRepairLoopAvailable);
            Assert.True(report.SolidWorksMacroRecordingRequestAvailable);
            Assert.True(report.SolidWorksPlateFeatureBuilderExists);
            Assert.True(report.SolidWorksRealDrawingBasicViewsImplemented);
            Assert.True(report.SolidWorksRealDrawingDefaultDisabled);
            Assert.True(report.SolidWorksRealDrawingRequiresEnvFlag);
            Assert.False(report.SolidWorksRealDrawingSmokeTestAttempted);
            Assert.False(report.SolidWorksRealDrawingSmokeTestPassed);
            Assert.Null(report.SolidWorksRealDrawingSmokeTestError);
            Assert.False(report.SolidWorksRealDrawingOutputsSlddrw);
            Assert.False(report.SolidWorksRealDrawingOutputsPdf);
            Assert.False(report.SolidWorksRealDrawingOutputsJsonReport);
            Assert.True(report.SolidWorksRealDrawingNotCalledInDefaultSelfCheck);
            Assert.False(report.SolidWorksDrawingReportGenerated);
            Assert.Null(report.SolidWorksRealDrawingFailureStage);
            Assert.True(report.SolidWorksDrawingFailureStageActionable);
            Assert.True(report.V11VersionStageDocumented);
            Assert.True(report.SolidWorksDrawingFailureRepairDocumented);
            Assert.True(report.SolidWorksDrawingApiEvidenceDocumented);
            Assert.True(report.SolidWorksDrawingReviewChecklistUpdated);
            Assert.True(report.SolidWorksRealDrawingDimensionsImplemented);
            Assert.True(report.SolidWorksRealDrawingDimensionsDefaultDisabled);
            Assert.True(report.SolidWorksRealDrawingDimensionsRequiresEnvFlag);
            Assert.False(report.SolidWorksRealDrawingDimensionsSmokeTestAttempted);
            Assert.False(report.SolidWorksRealDrawingDimensionsSmokeTestPassed);
            Assert.Null(report.SolidWorksRealDrawingDimensionsSmokeTestError);
            Assert.False(report.SolidWorksRealDrawingDimensionsOutputsSlddrw);
            Assert.False(report.SolidWorksRealDrawingDimensionsOutputsPdf);
            Assert.False(report.SolidWorksRealDrawingDimensionsOutputsJsonReport);
            Assert.True(report.SolidWorksRealDrawingDimensionsNotCalledInDefaultSelfCheck);
            Assert.False(report.SolidWorksDrawingDimensionReportGenerated);
            Assert.Null(report.SolidWorksRealDrawingDimensionFailureStage);
            Assert.True(report.SolidWorksDrawingDimensionFailureStageActionable);
            Assert.Null(report.RealDrawingDimensionOutputDirectory);
            Assert.Null(report.RealDrawingDimensionLatestReportPath);
            Assert.True(report.V12VersionStageDocumented);
            Assert.True(report.SolidWorksDrawingDimensionFailureRepairDocumented);
            Assert.True(report.SolidWorksDrawingDimensionApiEvidenceDocumented);
            Assert.True(report.SolidWorksDrawingDimensionReviewChecklistUpdated);
            Assert.True(report.SolidWorksRealDrawingTitleBlockImplemented);
            Assert.True(report.SolidWorksRealDrawingTitleBlockDefaultDisabled);
            Assert.True(report.SolidWorksRealDrawingTitleBlockRequiresEnvFlag);
            Assert.False(report.SolidWorksRealDrawingTitleBlockSmokeTestAttempted);
            Assert.False(report.SolidWorksRealDrawingTitleBlockSmokeTestPassed);
            Assert.Null(report.SolidWorksRealDrawingTitleBlockSmokeTestError);
            Assert.False(report.SolidWorksRealDrawingTitleBlockOutputsSlddrw);
            Assert.False(report.SolidWorksRealDrawingTitleBlockOutputsPdf);
            Assert.False(report.SolidWorksRealDrawingTitleBlockOutputsJsonReport);
            Assert.True(report.SolidWorksRealDrawingTitleBlockNotCalledInDefaultSelfCheck);
            Assert.False(report.SolidWorksDrawingTitleBlockReportGenerated);
            Assert.Null(report.SolidWorksRealDrawingTitleBlockFailureStage);
            Assert.True(report.SolidWorksDrawingTitleBlockFailureStageActionable);
            Assert.Null(report.RealDrawingTitleBlockOutputDirectory);
            Assert.Null(report.RealDrawingTitleBlockLatestReportPath);
            Assert.True(report.V13VersionStageDocumented);
            Assert.True(report.SolidWorksDrawingTitleBlockFailureRepairDocumented);
            Assert.True(report.SolidWorksDrawingTitleBlockApiEvidenceDocumented);
            Assert.True(report.SolidWorksDrawingTitleBlockReviewChecklistUpdated);
            Assert.Equal("custom_properties_only", report.SolidWorksDrawingTitleBlockPopulationStrategy);
            Assert.False(report.SolidWorksDrawingTitleBlockFieldsVerifiedInSheetFormat);
            Assert.True(report.SolidWorksReleasePackageImplemented);
            Assert.True(report.SolidWorksReleasePackageDefaultNoCadExecution);
            Assert.True(report.SolidWorksReleaseManifestGenerated);
            Assert.True(report.SolidWorksPackageQualityReportGenerated);
            Assert.True(report.SolidWorksReleaseSummaryGenerated);
            Assert.True(report.SolidWorksReleasePackageFailureStageActionable);
            Assert.NotNull(report.SolidWorksReleaseManifestPath);
            Assert.NotNull(report.SolidWorksPackageQualityReportPath);
            Assert.NotNull(report.SolidWorksReleaseSummaryPath);
            Assert.True(File.Exists(report.SolidWorksReleaseManifestPath));
            Assert.True(File.Exists(report.SolidWorksPackageQualityReportPath));
            Assert.True(File.Exists(report.SolidWorksReleaseSummaryPath));
            Assert.True(report.V14VersionStageDocumented);
            Assert.True(report.SolidWorksReleasePackageFailureRepairDocumented);
            Assert.True(report.SolidWorksReleasePackageReviewChecklistUpdated);
            Assert.True(report.RealCadWorkerIntegratedIntoMainWorkflow);
            Assert.True(report.ChiefEngineerOrchestratorInvokesCadWorkflow);
            Assert.True(report.WorkflowEngineCanRouteToSolidWorksWorker);
            Assert.True(report.RealCadMainWorkflowDefaultDisabled);
            Assert.True(report.RealCadMainWorkflowRequiresRequestFlag);
            Assert.True(report.RealCadMainWorkflowRequiresEnvFlag);
            Assert.True(report.RealCadMainWorkflowPassesQualityGate);
            Assert.True(report.GatewayDoesNotCallWorkerDirectly);
            Assert.True(report.LlmDoesNotCallWorkerDirectly);
            Assert.True(report.ReleasePackageAllSourceReportsPassedFieldExists);
            Assert.True(report.ReleasePackageDeliverableStatusFieldExists);
            Assert.True(report.ReleasePackageFailedSourceReportsBlockDeliverable);
            Assert.True(report.V15VersionStageDocumented);
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

    [Fact]
    public async Task SelfCheckRealBuildSmokeTestReportsFailureDiagnosticsWithoutStrictMode()
    {
        var root = FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(root);
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_solidworks_real", Guid.NewGuid().ToString("N"));

        using var _ = new EnvironmentScope(new Dictionary<string, string?>
        {
            ["SW_ENABLE_REAL_EXECUTION"] = "true",
            ["SW_REAL_BUILD_SMOKE_TEST"] = "true",
            ["SW_STRICT_REAL_BUILD_TEST"] = null,
            ["SW_TEMPLATE_PART_PATH"] = null,
            ["SW_OUTPUT_DIRECTORY"] = null
        });

        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, root);

            Assert.Equal("true", report.SwEnableRealExecutionEnvValue);
            Assert.Equal("true", report.SwRealBuildSmokeTestEnvValue);
            Assert.True(report.SolidWorksRealBuildSmokeTestAttempted);
            Assert.False(report.SolidWorksRealBuildSmokeTestPassed);
            Assert.False(report.RealBuildRequestDryRun);
            Assert.True(report.RealBuildRequestAllowRealCadExecution);
            Assert.Equal("RealBuildPlateBasic4Holes", report.RealBuildExecutionMode);
            Assert.NotNull(report.RealBuildOutputDirectory);
            Assert.True(Path.IsPathFullyQualified(report.RealBuildOutputDirectory));
            Assert.Contains(Path.Combine("output", "solidworks", "real", "plate_basic_4holes"), report.RealBuildOutputDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(report.RealBuildLatestReportPath);
            Assert.True(File.Exists(report.RealBuildLatestReportPath));
            Assert.Contains("template_part_path_required_for_real_build", report.SolidWorksRealBuildSmokeTestError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preflight_failed", report.SolidWorksRealBuildFailureStage);
            Assert.True(report.SolidWorksRealBuildErrorIsActionable);
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

    [Fact]
    public void SolidWorksSmokeRunnerProjectExists()
    {
        var projectRoot = FindProjectRoot();

        Assert.True(File.Exists(Path.Combine(
            projectRoot,
            "tools",
            "SolidWorksSmokeRunner",
            "SolidWorksSmokeRunner.csproj")));
    }

    [Fact]
    public void SolidWorksDiagnosticReportSchemaCanSerialize()
    {
        var report = new SolidWorksDiagnosticReport
        {
            OutputDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "solidworks-diagnostics-schema")),
            SolidWorksConnected = false,
            FailureStage = "connection_failed",
            FinalStatus = "Failed",
            SldprtPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "plate_basic_4holes.SLDPRT")),
            StepPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "plate_basic_4holes.STEP"))
        };
        report.Errors.Add("connection_failed: SldWorks.Application COM ProgID was not found.");
        report.Operations.Add(new SolidWorksDiagnosticOperation
        {
            Name = "connection_started",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Success = false,
            Error = "connection_failed",
            ElapsedMs = 1
        });

        var json = JsonSerializer.Serialize(report);

        Assert.Contains("failure_stage", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connection_failed", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("save_sldprt_attempted", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export_step_attempted", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plane_selection_attempted", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plane_selection_success", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("available_reference_planes", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api_evidence_report_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api_repair_attempted", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api_repair_strategy", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repair_failure_reason", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolidWorksApiFailureAnalyzerRecognizesCutHolesFailure()
    {
        var analysis = new SolidWorksApiFailureAnalyzer().AnalyzeFailureStage("cut_holes_failed");

        Assert.Equal("cut_holes_failed", analysis.FailureStage);
        Assert.Contains("FeatureExtrusion2", string.Join(" ", analysis.RecommendedApiSearchTerms), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FeatureCut4", string.Join(" ", analysis.RecommendedApiSearchTerms), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CreateCircleByRadius", string.Join(" ", analysis.RecommendedApiSearchTerms), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(analysis.SuspectedCauses, cause => cause.Contains("参数", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApiEvidenceReportSchemaCanSerialize()
    {
        var report = new ApiEvidenceReport(
            "api-evidence-test",
            DateTimeOffset.UtcNow,
            "cut_holes_failed",
            "through-hole cut",
            new[]
            {
                new ApiEvidenceSource(
                    "official_api",
                    "IFeatureManager.FeatureCut4",
                    "https://help.solidworks.com/",
                    new[] { "FeatureCut4" },
                    "FeatureCut4 用于切除拉伸。",
                    "官方文档仅作为事实来源。",
                    CanReuseCode: false,
                    CanReuseIdea: true)
            },
            Array.Empty<ApiEvidenceSource>(),
            Array.Empty<ApiEvidenceSource>(),
            new[]
            {
                new ApiCandidate(
                    "FeatureManager.FeatureCut4",
                    "切除四个通孔。",
                    new[] { "创建孔草图", "执行切除" },
                    new[] { "草图已创建" },
                    "草图或轮廓可被识别",
                    "返回 Feature",
                    new[] { "参数数量不匹配" },
                    "运行诊断 Runner")
            },
            "featurecut4_reference_signature_with_flip_retry",
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            "按证据修复切孔封装。");

        var json = JsonSerializer.Serialize(report);

        Assert.Contains("selected_api_strategy", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required_selection_state", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SolidWorksApiEvidenceCollectorHandlesMissingReferenceRepositoryWithoutCopyingScripts()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "solidworks_api_evidence_test", Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(tempRoot, "project");
        var outputRoot = Path.Combine(tempRoot, "output-root");
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "references", "external"));
            File.WriteAllText(
                Path.Combine(projectRoot, "references", "external", "solidworks-automation-skill-analysis.md"),
                "# SolidWorks 自动化 Skill 分析\n\n该文档说明 preflight、Worker 封装和 QualityGate 边界。");

            var analysis = new SolidWorksApiFailureAnalyzer().AnalyzeFailureStage("cut_holes_failed");
            var result = new SolidWorksApiEvidenceCollector().CollectCutHolesEvidence(
                projectRoot,
                outputRoot,
                analysis,
                "测试中的原始 FeatureCut4 调用失败。");

            Assert.True(File.Exists(result.JsonReportPath));
            Assert.True(File.Exists(result.MarkdownReportPath));
            Assert.True(File.Exists(result.MacroRecordingRequestPath));
            Assert.True(result.ReferenceSkillAnalysisRead);
            Assert.False(result.ReferenceRepositoryFound);
            Assert.False(result.ExternalScriptsCopied);
            Assert.Equal("macro_recorded_active_sketch_featurecut4_after_base_extrusion", result.Report.SelectedApiStrategy);
            Assert.Contains("FeatureCut4", result.Report.FinalRecommendation, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FeatureExtrusion2", result.Report.FinalRecommendation, StringComparison.OrdinalIgnoreCase);

            var markdown = File.ReadAllText(result.MarkdownReportPath);
            Assert.Contains("API 证据报告", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("不复制", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.EnumerateFiles(tempRoot, "*.py", SearchOption.AllDirectories).Any());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksPlateFeatureBuilderExposesThroughHoleRepairMethod()
    {
        Assert.NotNull(typeof(SolidWorksPlateFeatureBuilder).GetMethod(nameof(SolidWorksPlateFeatureBuilder.CreateThroughHoles)));
        Assert.NotNull(typeof(SolidWorksPlateFeatureBuilder).GetMethod(
            "SelectSketchForFeatureCut",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        Assert.Equal(1, SolidWorksDiagnosticRunner.MaxRepairAttempts);
    }

    [Fact]
    public void SolidWorksPlateFeatureBuilderSavePartThrowsWhenAllSaveMethodsFail()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_savepart_false_test", Guid.NewGuid().ToString("N"));
        var partPath = Path.Combine(root, "plate_basic_4holes.SLDPRT");
        var builder = new SolidWorksPlateFeatureBuilder();
        var diagnostics = new SolidWorksPlateBuildDiagnostics();
        var logs = new List<string>();

        try
        {
            var exception = Assert.Throws<IOException>(() => builder.SavePart(
                new object(),
                partPath,
                diagnostics,
                logs,
                (_, _, _, _) => false,
                (_, _, _) => false));

            Assert.Contains("sldprt_save_failed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(diagnostics.SldprtSaveAttempted);
            Assert.False(diagnostics.SldprtSaveSuccess);
            Assert.Equal(Path.GetFullPath(partPath), diagnostics.SldprtPath);
            Assert.Contains(diagnostics.SldprtSaveErrors, error => error.Contains("SaveAs returned false", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(diagnostics.Issues, issue => issue.Contains("sldprt_save_failed", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("save_sldprt_success", diagnostics.OperationsExecuted);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksPlateFeatureBuilderSavePartRejectsEmptySldprtFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "solidworks_savepart_empty_test", Guid.NewGuid().ToString("N"));
        var partPath = Path.Combine(root, "plate_basic_4holes.SLDPRT");
        var builder = new SolidWorksPlateFeatureBuilder();
        var diagnostics = new SolidWorksPlateBuildDiagnostics();
        var logs = new List<string>();

        try
        {
            var exception = Assert.Throws<IOException>(() => builder.SavePart(
                new object(),
                partPath,
                diagnostics,
                logs,
                (_, savePath, _, _) =>
                {
                    File.WriteAllBytes(savePath, Array.Empty<byte>());
                    return true;
                },
                (_, _, _) => false));

            Assert.Contains("sldprt_save_failed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(diagnostics.SldprtSaveAttempted);
            Assert.False(diagnostics.SldprtSaveSuccess);
            Assert.True(File.Exists(partPath));
            Assert.Equal(0, new FileInfo(partPath).Length);
            Assert.Contains(diagnostics.Issues, issue => issue.Contains("SLDPRT file was not created or is empty", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("save_sldprt_success", diagnostics.OperationsExecuted);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksDiagnosticRunnerSeparatesBuildAndRepairLogPrefixes()
    {
        var copyDiagnostics = typeof(SolidWorksDiagnosticRunner).GetMethod(
            "CopyRepairDiagnostics",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(copyDiagnostics);

        var diagnostics = new SolidWorksPlateBuildDiagnostics();
        var buildReport = new SolidWorksDiagnosticReport();
        copyDiagnostics.Invoke(
            null,
            new object[]
            {
                buildReport,
                diagnostics,
                new[] { "normal build path log" },
                "build_log"
            });

        Assert.Contains(buildReport.Warnings, warning => warning.Contains("build_log: normal build path log", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(buildReport.Warnings, warning => warning.Contains("repair_log:", StringComparison.OrdinalIgnoreCase));

        var repairReport = new SolidWorksDiagnosticReport();
        copyDiagnostics.Invoke(
            null,
            new object[]
            {
                repairReport,
                diagnostics,
                new[] { "repair path log" },
                "repair_log"
            });

        Assert.Contains(repairReport.Warnings, warning => warning.Contains("repair_log: repair path log", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SolidWorksPlaneSelectorExposesEnglishAndLocalizedCandidates()
    {
        Assert.Contains("Top Plane", SolidWorksPlaneSelector.EnglishPlaneNames);
        Assert.Contains("Front Plane", SolidWorksPlaneSelector.EnglishPlaneNames);
        Assert.Contains("Right Plane", SolidWorksPlaneSelector.EnglishPlaneNames);
        Assert.Contains("上视基准面", SolidWorksPlaneSelector.LocalizedPlaneNames);
        Assert.Contains("前视基准面", SolidWorksPlaneSelector.LocalizedPlaneNames);
        Assert.Contains("右视基准面", SolidWorksPlaneSelector.LocalizedPlaneNames);
    }

    [Fact]
    public void SolidWorksPlaneSelectorFallsBackToLocalizedNameWhenEnglishFails()
    {
        var extension = new FakeSolidWorksModelExtension(new Dictionary<string, bool>
        {
            ["上视基准面"] = true
        });
        var model = new FakeSolidWorksModel(extension, firstFeature: null);

        var result = new SolidWorksPlaneSelector().SelectStandardPlane(model);

        Assert.True(result.Success);
        Assert.Equal("上视基准面", result.SelectedPlaneName);
        Assert.Equal("named_localized", result.SelectedPlaneStrategy);
        Assert.Contains("Top Plane", extension.AttemptedNames);
        Assert.Contains("Front Plane", extension.AttemptedNames);
        Assert.Contains("Right Plane", extension.AttemptedNames);
        Assert.Contains("上视基准面", extension.AttemptedNames);
    }

    [Fact]
    public void SolidWorksPlaneSelectorScansFeatureManagerWhenNameSelectionFails()
    {
        var extension = new FakeSolidWorksModelExtension(new Dictionary<string, bool>());
        var feature = new FakeSolidWorksFeature("前视基准面", "RefPlane", selectSuccess: true);
        var model = new FakeSolidWorksModel(extension, feature);

        var result = new SolidWorksPlaneSelector().SelectStandardPlane(model);

        Assert.True(result.Success);
        Assert.Equal("前视基准面", result.SelectedPlaneName);
        Assert.Equal("feature_scan_preferred", result.SelectedPlaneStrategy);
        Assert.Contains(result.AvailableReferencePlanes, plane =>
            plane.Name == "前视基准面" &&
            plane.TypeName == "RefPlane" &&
            plane.SelectSuccess == true);
    }

    [Theory]
    [InlineData("solidworks_connection_failed", "connection_failed")]
    [InlineData("new_part_failed: NewDocument returned null", "new_part_failed")]
    [InlineData("sketch_failed: SelectByID2 failed", "sketch_failed")]
    [InlineData("extrude_failed: FeatureExtrusion2 returned null", "extrude_failed")]
    [InlineData("cut_holes_failed: FeatureCut4 returned null", "cut_holes_failed")]
    [InlineData("sldprt_save_failed: SaveAs returned false", "save_sldprt_failed")]
    [InlineData("step_export_failed: STEP file missing", "export_step_failed")]
    public void SolidWorksSmokeRunnerMapsFailureStage(string error, string expectedStage)
    {
        Assert.Equal(expectedStage, SolidWorksDiagnosticRunner.MapFailureStage(error));
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), $"Condition was not met within {timeout}.");
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

    private sealed class CountingSolidWorksSessionManager : ISolidWorksSessionManager
    {
        private readonly bool _connectsSuccessfully;

        public CountingSolidWorksSessionManager(bool connectsSuccessfully = false)
        {
            _connectsSuccessfully = connectsSuccessfully;
        }

        public int ConnectAttempts { get; private set; }

        public int ExecuteWithApplicationAttempts { get; private set; }

        public int DisconnectAttempts { get; private set; }

        public bool WaitForDisconnectCancellation { get; init; }

        public bool ThrowExecutionTimeout { get; init; }

        public bool DisconnectTokenCanBeCanceled { get; private set; }

        public bool DisconnectCancellationObserved { get; private set; }

        public Task<SolidWorksSessionConnectionResult> ConnectAsync(
            SolidWorksRuntimeOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectAttempts++;

            return Task.FromResult(new SolidWorksSessionConnectionResult(
                _connectsSuccessfully,
                _connectsSuccessfully ? "TestVersion" : null,
                _connectsSuccessfully ? Array.Empty<string>() : new[] { "solidworks_connection_failed" },
                new[] { "fake session manager used by tests" }));
        }

        public async Task<T> ExecuteWithApplicationAsync<T>(
            Func<object, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default,
            int? executionTimeoutSeconds = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteWithApplicationAttempts++;

            if (!_connectsSuccessfully)
            {
                throw new InvalidOperationException("solidworks_application_missing: active SolidWorks COM session is not connected.");
            }

            if (ThrowExecutionTimeout)
            {
                throw new TimeoutException("solidworks_execution_timeout: SolidWorks execution timed out after 1 seconds.");
            }

            return await action(new object(), cancellationToken);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectAttempts++;
            DisconnectTokenCanBeCanceled = cancellationToken.CanBeCanceled;

            if (!WaitForDisconnectCancellation)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                DisconnectCancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class FakeSolidWorksModel
    {
        private readonly FakeSolidWorksFeature? _firstFeature;

        public FakeSolidWorksModel(FakeSolidWorksModelExtension extension, FakeSolidWorksFeature? firstFeature)
        {
            Extension = extension;
            _firstFeature = firstFeature;
        }

        public FakeSolidWorksModelExtension Extension { get; }

        public FakeSolidWorksFeature? FirstFeature() => _firstFeature;
    }

    private sealed class FakeSolidWorksModelExtension
    {
        private readonly IReadOnlyDictionary<string, bool> _selectionResults;

        public FakeSolidWorksModelExtension(IReadOnlyDictionary<string, bool> selectionResults)
        {
            _selectionResults = selectionResults;
        }

        public List<string> AttemptedNames { get; } = [];

        public bool SelectByID2(
            string name,
            string type,
            double x,
            double y,
            double z,
            bool append,
            int mark,
            object? callout,
            int option)
        {
            AttemptedNames.Add(name);
            return type == "PLANE" &&
                   _selectionResults.TryGetValue(name, out var selected) &&
                   selected;
        }
    }

    private sealed class FakeSolidWorksFeature
    {
        private readonly bool _selectSuccess;
        private FakeSolidWorksFeature? _nextFeature;

        public FakeSolidWorksFeature(string name, string typeName, bool selectSuccess)
        {
            Name = name;
            TypeName = typeName;
            _selectSuccess = selectSuccess;
        }

        public string Name { get; }

        public string TypeName { get; }

        public string GetTypeName2() => TypeName;

        public string GetTypeName() => TypeName;

        public bool Select2(bool append, int mark) => _selectSuccess;

        public FakeSolidWorksFeature? GetNextFeature() => _nextFeature;

        public FakeSolidWorksFeature WithNext(FakeSolidWorksFeature nextFeature)
        {
            _nextFeature = nextFeature;
            return this;
        }
    }

    private sealed class FakeTitleBlockCustomPropertyManager
    {
        private readonly string _readBackValue;

        public FakeTitleBlockCustomPropertyManager(string readBackValue)
        {
            _readBackValue = readBackValue;
        }

        public int Add3(string name, int type, string value, int options) => 0;

        public int Get6(
            string name,
            bool useCached,
            ref string value,
            ref string resolvedValue,
            ref bool wasResolved,
            ref bool linkToProperty)
        {
            value = _readBackValue;
            resolvedValue = _readBackValue;
            wasResolved = true;
            linkToProperty = false;
            return 0;
        }
    }

    private sealed class FakeTitleBlockDrawingDocument
    {
        public FakeTitleBlockDrawingExtension Extension { get; } = new();
    }

    private sealed class FakeTitleBlockDrawingExtension
    {
        public bool SaveAs(
            string path,
            int version,
            int options,
            object? exportData,
            int errors,
            int warnings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Array.Empty<byte>());
            return true;
        }
    }

    private sealed class TestSolidWorksPlateBuilder : ISolidWorksPlateBuilder
    {
        public int BuildAttempts { get; private set; }

        public async Task<SolidWorksPlateBuildResult> BuildPlateBasicFourHolesAsync(
            object application,
            SolidWorksWorkerRequest request,
            SolidWorksRuntimeOptions options,
            string? solidWorksVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildAttempts++;

            var outputDirectory = SolidWorksPlateBuildOutput.ResolveOutputDirectory(request, options);
            Directory.CreateDirectory(outputDirectory);
            var partPath = Path.Combine(outputDirectory, "plate_basic_4holes.SLDPRT");
            var stepPath = Path.Combine(outputDirectory, "plate_basic_4holes.STEP");
            var reportPath = Path.Combine(outputDirectory, "build_report.json");

            await File.WriteAllTextAsync(partPath, "fake real SolidWorks part bytes for tests", cancellationToken);
            await File.WriteAllTextAsync(stepPath, "fake real STEP bytes for tests", cancellationToken);
            var diagnostics = new SolidWorksPlateBuildDiagnostics
            {
                SldprtSaveAttempted = true,
                SldprtSaveSuccess = true,
                SldprtPath = partPath,
                SldprtSizeBytes = new FileInfo(partPath).Length,
                StepExportAttempted = true,
                StepExportSuccess = true,
                StepPath = stepPath,
                StepSizeBytes = new FileInfo(stepPath).Length,
                ActiveDocTitleBeforeStepExport = "plate_basic_4holes",
                ActiveDocTitleAfterActivate = "plate_basic_4holes",
                PlaneSelectionAttempted = true,
                PlaneSelectionSuccess = true,
                SelectedPlaneName = "上视基准面",
                SelectedPlaneStrategy = "named_localized"
            };
            diagnostics.AvailableReferencePlanes.Add(new SolidWorksReferencePlaneInfo("上视基准面", "RefPlane", true));
            diagnostics.OperationsExecuted.AddRange(new[]
            {
                "real_build_request_received",
                "safety_flags_checked",
                "new_part_success",
                "plane_selection_started",
                "plane_selection_success",
                "save_sldprt_success",
                "export_step_success",
                "build_report_written"
            });
            await SolidWorksPlateBuildReportWriter.WriteAsync(
                reportPath,
                request,
                "RealBuildPlateBasic4Holes",
                realCadExecuted: true,
                realCadConnected: true,
                solidWorksVersion,
                outputDirectory,
                new[] { partPath, stepPath },
                diagnostics,
                "Passed",
                cancellationToken);

            return SolidWorksPlateBuildResult.Completed(
                new[]
                {
                    SolidWorksPlateBuildOutput.Artifact("real-part", "Part", partPath, ".SLDPRT", "真实 SolidWorks 零件文件。"),
                    SolidWorksPlateBuildOutput.Artifact("real-step", "Step", stepPath, ".STEP", "真实 STEP 导出文件。"),
                    SolidWorksPlateBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "真实构建报告。")
                },
                new[] { "test plate builder generated controlled artifacts" });
        }
    }

    private sealed class TestSolidWorksDrawingBuilder : ISolidWorksDrawingBuilder
    {
        public int BuildAttempts { get; private set; }

        public async Task<SolidWorksDrawingBuildResult> CreateBasicViewsDrawingAsync(
            object application,
            SolidWorksWorkerRequest request,
            SolidWorksRuntimeOptions options,
            string? solidWorksVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildAttempts++;

            var outputDirectory = SolidWorksDrawingBuildOutput.ResolveOutputDirectory(request, options);
            Directory.CreateDirectory(outputDirectory);
            var drawingPath = Path.Combine(outputDirectory, "plate_basic_4holes.SLDDRW");
            var pdfPath = Path.Combine(outputDirectory, "plate_basic_4holes.pdf");
            var reportPath = Path.Combine(outputDirectory, "drawing_report.json");

            await File.WriteAllTextAsync(drawingPath, "fake real SolidWorks drawing bytes for tests", cancellationToken);
            await File.WriteAllTextAsync(pdfPath, "fake real drawing PDF bytes for tests", cancellationToken);

            var report = new SolidWorksDrawingReport
            {
                SourcePartPath = request.SourcePartPath,
                OutputDirectory = outputDirectory,
                SolidWorksConnected = true,
                SolidWorksVersion = solidWorksVersion,
                DrawingCreated = true,
                SlddrwPath = drawingPath,
                SlddrwExists = true,
                SlddrwSizeBytes = new FileInfo(drawingPath).Length,
                PdfPath = pdfPath,
                PdfExists = true,
                PdfSizeBytes = new FileInfo(pdfPath).Length,
                CompletedAt = DateTimeOffset.UtcNow,
                FinalStatus = "Passed"
            };
            report.ViewsCreated.AddRange(new[] { "Front", "Top", "Right", "Isometric" });
            report.Operations.AddRange(new[]
            {
                "drawing_request_received",
                "source_part_open_success",
                "front_view_create_success",
                "top_view_create_success",
                "right_view_create_success",
                "isometric_view_create_success",
                "slddrw_save_success",
                "pdf_export_success",
                "drawing_report_written"
            });
            await SolidWorksDrawingReportWriter.WriteAsync(reportPath, report, cancellationToken);

            return SolidWorksDrawingBuildResult.Completed(
                new[]
                {
                    SolidWorksDrawingBuildOutput.Artifact("real-drawing", "Drawing", drawingPath, ".SLDDRW", "真实 SolidWorks 工程图文件。"),
                    SolidWorksDrawingBuildOutput.Artifact("real-drawing-pdf", "Pdf", pdfPath, ".pdf", "真实工程图 PDF 导出文件。"),
                    SolidWorksDrawingBuildOutput.Artifact("real-drawing-report", "DrawingReport", reportPath, ".json", "真实工程图报告。")
                },
                new[] { "test drawing builder generated controlled artifacts" });
        }
    }

    private sealed class TestSolidWorksDrawingDimensionBuilder : ISolidWorksDrawingDimensionBuilder
    {
        public int BuildAttempts { get; private set; }

        public async Task<SolidWorksDrawingDimensionBuildResult> CreateDimensionedDrawingAsync(
            object application,
            SolidWorksWorkerRequest request,
            SolidWorksRuntimeOptions options,
            string? solidWorksVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildAttempts++;

            var outputDirectory = SolidWorksDrawingDimensionBuildOutput.ResolveOutputDirectory(request, options);
            Directory.CreateDirectory(outputDirectory);
            var drawingPath = Path.Combine(outputDirectory, "plate_basic_4holes_dimensioned.SLDDRW");
            var pdfPath = Path.Combine(outputDirectory, "plate_basic_4holes_dimensioned.pdf");
            var reportPath = Path.Combine(outputDirectory, "dimension_report.json");

            await File.WriteAllTextAsync(drawingPath, "fake dimensioned SolidWorks drawing bytes for tests", cancellationToken);
            await File.WriteAllTextAsync(pdfPath, "fake dimensioned drawing PDF bytes for tests", cancellationToken);

            var report = new SolidWorksDrawingDimensionReport
            {
                SourceDrawingPath = request.SourceDrawingPath,
                OutputDirectory = outputDirectory,
                SolidWorksConnected = true,
                SolidWorksVersion = solidWorksVersion,
                DrawingOpened = true,
                LengthDimensionAdded = true,
                WidthDimensionAdded = true,
                ThicknessDimensionAdded = true,
                HoleDiameterDimensionAdded = true,
                HolePositionDimensionAdded = true,
                SlddrwPath = drawingPath,
                SlddrwExists = true,
                SlddrwSizeBytes = new FileInfo(drawingPath).Length,
                PdfPath = pdfPath,
                PdfExists = true,
                PdfSizeBytes = new FileInfo(pdfPath).Length,
                CompletedAt = DateTimeOffset.UtcNow,
                FinalStatus = "Passed"
            };
            report.ViewsConfirmed.AddRange(new[] { "Front", "Top", "Right", "Isometric" });
            report.Dimensions.AddRange(new[]
            {
                new SolidWorksDrawingDimensionResult("plate_length", 160, "Passed", "length_dimension_failed", "IDrawingDoc.CreateLinearDim4_non_associative", "test dimension added"),
                new SolidWorksDrawingDimensionResult("plate_width", 80, "Passed", "width_dimension_failed", "IDrawingDoc.CreateLinearDim4_non_associative", "test dimension added"),
                new SolidWorksDrawingDimensionResult("plate_thickness", 12, "Passed", "thickness_dimension_failed", "IDrawingDoc.CreateLinearDim4_non_associative", "test dimension added"),
                new SolidWorksDrawingDimensionResult("hole_diameter", 10, "Passed", "hole_diameter_dimension_failed", "IDrawingDoc.ICreateDiamDim4_non_associative", "test dimension added"),
                new SolidWorksDrawingDimensionResult("hole_center_distance_x", 120, "Passed", "hole_position_dimension_failed", "IDrawingDoc.CreateLinearDim4_non_associative", "test dimension added"),
                new SolidWorksDrawingDimensionResult("hole_center_distance_y", 40, "Passed", "hole_position_dimension_failed", "IDrawingDoc.CreateLinearDim4_non_associative", "test dimension added")
            });
            report.Operations.AddRange(new[]
            {
                "drawing_dimension_request_received",
                "source_drawing_open_success",
                "drawing_views_confirm_success",
                "length_dimension_success",
                "width_dimension_success",
                "thickness_dimension_success",
                "hole_diameter_dimension_success",
                "hole_position_dimension_success",
                "dimension_save_success",
                "dimension_pdf_export_success",
                "dimension_report_written"
            });
            await SolidWorksDrawingDimensionReportWriter.WriteAsync(reportPath, report, cancellationToken);

            return SolidWorksDrawingDimensionBuildResult.Completed(
                new[]
                {
                    SolidWorksDrawingDimensionBuildOutput.Artifact("real-dimensioned-drawing", "Drawing", drawingPath, ".SLDDRW", "带基础尺寸的真实 SolidWorks 工程图文件。"),
                    SolidWorksDrawingDimensionBuildOutput.Artifact("real-dimensioned-drawing-pdf", "Pdf", pdfPath, ".pdf", "带基础尺寸的工程图 PDF。"),
                    SolidWorksDrawingDimensionBuildOutput.Artifact("real-dimension-report", "DimensionReport", reportPath, ".json", "真实工程图基础尺寸报告。")
                },
                new[] { "test drawing dimension builder generated controlled artifacts" });
        }
    }

    private sealed class TestSolidWorksDrawingTitleBlockBuilder : ISolidWorksDrawingTitleBlockBuilder
    {
        public int BuildAttempts { get; private set; }

        public async Task<SolidWorksDrawingTitleBlockBuildResult> ApplyTitleBlockAsync(
            object application,
            SolidWorksWorkerRequest request,
            SolidWorksRuntimeOptions options,
            string? solidWorksVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildAttempts++;

            var outputDirectory = SolidWorksDrawingTitleBlockBuildOutput.ResolveOutputDirectory(request, options);
            Directory.CreateDirectory(outputDirectory);
            var drawingPath = Path.Combine(outputDirectory, "plate_basic_4holes_title_block.SLDDRW");
            var pdfPath = Path.Combine(outputDirectory, "plate_basic_4holes_title_block.pdf");
            var reportPath = Path.Combine(outputDirectory, "title_block_report.json");

            await File.WriteAllTextAsync(drawingPath, "fake title block SolidWorks drawing bytes for tests", cancellationToken);
            await File.WriteAllTextAsync(pdfPath, "fake title block drawing PDF bytes for tests", cancellationToken);

            var report = new SolidWorksDrawingTitleBlockReport
            {
                SourceDimensionedDrawingPath = request.SourceDimensionedDrawingPath,
                OutputDirectory = outputDirectory,
                SolidWorksConnected = true,
                SolidWorksVersion = solidWorksVersion,
                DrawingOpened = true,
                TitleBlockTemplateDetected = true,
                DrawingPropertiesRead = true,
                CustomPropertiesWritten = true,
                TitleBlockUpdated = true,
                PartName = "plate_basic_4holes",
                DrawingNumber = "PLATE-BASIC-4HOLES",
                Material = "Q235",
                Scale = "1:1",
                DrawingDate = "2026-07-06",
                Revision = "A",
                SlddrwPath = drawingPath,
                SlddrwExists = true,
                SlddrwSizeBytes = new FileInfo(drawingPath).Length,
                PdfPath = pdfPath,
                PdfExists = true,
                PdfSizeBytes = new FileInfo(pdfPath).Length,
                CompletedAt = DateTimeOffset.UtcNow,
                FinalStatus = "Passed"
            };
            report.Properties.AddRange(new[]
            {
                new SolidWorksDrawingTitleBlockProperty("PartName", "plate_basic_4holes", "Passed", "custom_property_write_failed", "IModelDocExtension.CustomPropertyManager_Add3_Set2_Get6", "test property written"),
                new SolidWorksDrawingTitleBlockProperty("DrawingNumber", "PLATE-BASIC-4HOLES", "Passed", "custom_property_write_failed", "IModelDocExtension.CustomPropertyManager_Add3_Set2_Get6", "test property written"),
                new SolidWorksDrawingTitleBlockProperty("Material", "Q235", "Passed", "custom_property_write_failed", "IModelDocExtension.CustomPropertyManager_Add3_Set2_Get6", "test property written"),
                new SolidWorksDrawingTitleBlockProperty("Scale", "1:1", "Passed", "custom_property_write_failed", "IModelDocExtension.CustomPropertyManager_Add3_Set2_Get6", "test property written"),
                new SolidWorksDrawingTitleBlockProperty("DrawingDate", "2026-07-06", "Passed", "custom_property_write_failed", "IModelDocExtension.CustomPropertyManager_Add3_Set2_Get6", "test property written"),
                new SolidWorksDrawingTitleBlockProperty("Revision", "A", "Passed", "custom_property_write_failed", "IModelDocExtension.CustomPropertyManager_Add3_Set2_Get6", "test property written")
            });
            report.Operations.AddRange(new[]
            {
                "drawing_title_block_request_received",
                "source_drawing_open_success",
                "title_block_template_detected",
                "drawing_properties_read_success",
                "custom_property_write_success",
                "title_block_update_success",
                "title_block_save_success",
                "title_block_pdf_export_success",
                "title_block_report_written"
            });
            await SolidWorksDrawingTitleBlockReportWriter.WriteAsync(reportPath, report, cancellationToken);

            return SolidWorksDrawingTitleBlockBuildResult.Completed(
                new[]
                {
                    SolidWorksDrawingTitleBlockBuildOutput.Artifact("real-title-block-drawing", "Drawing", drawingPath, ".SLDDRW", "SolidWorks drawing with title block metadata."),
                    SolidWorksDrawingTitleBlockBuildOutput.Artifact("real-title-block-drawing-pdf", "Pdf", pdfPath, ".pdf", "Title block drawing PDF."),
                    SolidWorksDrawingTitleBlockBuildOutput.Artifact("real-title-block-report", "TitleBlockReport", reportPath, ".json", "Title block report.")
                },
                new[] { "test drawing title block builder generated controlled artifacts" });
        }
    }

    private sealed class TrackingSolidWorksComActivator : ISolidWorksComActivator
    {
        private int _activeApplications;

        public Action<object, bool, CancellationToken>? SetVisibleAction { get; init; }

        public int CreatedApplications { get; private set; }

        public int ReleasedApplications { get; private set; }

        public int MaxConcurrentApplications { get; private set; }

        public object CreateApplication(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreatedApplications++;
            var active = Interlocked.Increment(ref _activeApplications);
            MaxConcurrentApplications = Math.Max(MaxConcurrentApplications, active);
            return new object();
        }

        public void SetVisible(object application, bool visible, CancellationToken cancellationToken)
        {
            if (SetVisibleAction is not null)
            {
                SetVisibleAction(application, visible, cancellationToken);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        public string? ReadVersion(object application, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return "TestVersion";
        }

        public void Release(object application)
        {
            ReleasedApplications++;
            Interlocked.Decrement(ref _activeApplications);
        }
    }

    private static async Task WriteReleasePackageFixtureAsync(string root, string? failedReportName = null)
    {
        var timestamp = "20260702_030717_684_fixture";
        var plateRoot = Path.Combine(root, "output", "solidworks", "real", "plate_basic_4holes", timestamp);
        var drawingRoot = Path.Combine(root, "output", "solidworks", "real", "plate_basic_4holes_drawing", timestamp);
        var dimensionRoot = Path.Combine(root, "output", "solidworks", "real", "plate_basic_4holes_drawing_dimensions", timestamp);
        var titleBlockRoot = Path.Combine(root, "output", "solidworks", "real", "plate_basic_4holes_title_block", timestamp);
        var diagnosticRoot = Path.Combine(root, "output", "solidworks", "diagnostics", "plate_basic_4holes", timestamp);

        await WriteFileAsync(Path.Combine(plateRoot, "plate_basic_4holes.SLDPRT"), "fake sldprt bytes");
        await WriteFileAsync(Path.Combine(plateRoot, "plate_basic_4holes.STEP"), "fake step bytes");
        await WriteReportAsync(Path.Combine(plateRoot, "build_report.json"), StatusFor("build_report.json", failedReportName), FailureStageFor("build_report.json", failedReportName));
        await WriteFileAsync(Path.Combine(drawingRoot, "plate_basic_4holes.SLDDRW"), "fake drawing bytes");
        await WriteFileAsync(Path.Combine(drawingRoot, "plate_basic_4holes.pdf"), "fake drawing pdf bytes");
        await WriteReportAsync(Path.Combine(drawingRoot, "drawing_report.json"), StatusFor("drawing_report.json", failedReportName), FailureStageFor("drawing_report.json", failedReportName));
        await WriteFileAsync(Path.Combine(dimensionRoot, "plate_basic_4holes_dimensioned.SLDDRW"), "fake dimensioned drawing bytes");
        await WriteFileAsync(Path.Combine(dimensionRoot, "plate_basic_4holes_dimensioned.pdf"), "fake dimensioned pdf bytes");
        await WriteReportAsync(Path.Combine(dimensionRoot, "dimension_report.json"), StatusFor("dimension_report.json", failedReportName), FailureStageFor("dimension_report.json", failedReportName));
        await WriteFileAsync(Path.Combine(titleBlockRoot, "plate_basic_4holes_title_block.SLDDRW"), "fake title block drawing bytes");
        await WriteFileAsync(Path.Combine(titleBlockRoot, "plate_basic_4holes_title_block.pdf"), "fake title block pdf bytes");
        await WriteReportAsync(Path.Combine(titleBlockRoot, "title_block_report.json"), StatusFor("title_block_report.json", failedReportName), FailureStageFor("title_block_report.json", failedReportName));
        await WriteReportAsync(Path.Combine(diagnosticRoot, "diagnostic_report.json"), StatusFor("diagnostic_report.json", failedReportName), FailureStageFor("diagnostic_report.json", failedReportName));
    }

    private static string StatusFor(string reportName, string? failedReportName) =>
        string.Equals(reportName, failedReportName, StringComparison.OrdinalIgnoreCase) ? "Failed" : "Passed";

    private static string? FailureStageFor(string reportName, string? failedReportName) =>
        string.Equals(reportName, failedReportName, StringComparison.OrdinalIgnoreCase) ? "source_report_failed_probe" : null;

    private static async Task WriteFileAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    private static async Task WriteReportAsync(string path, string finalStatus, string? failureStage)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new Dictionary<string, string?>
        {
            ["final_status"] = finalStatus,
            ["failure_stage"] = failureStage
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload));
    }

    private sealed class FailingSolidWorksWorker : ISolidWorksWorker
    {
        public string Name => nameof(FakeSolidWorksWorker);

        public string TargetSystem => "SolidWorks";

        public Task<SolidWorksWorkerResult> ExecuteAsync(
            SolidWorksWorkerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SolidWorksWorkerResult(
                request.RequestId,
                "Failed",
                Array.Empty<SolidWorksArtifact>(),
                new[] { "FailingSolidWorksWorker returned a controlled failed result for main workflow regression coverage." },
                new[] { "intentional_worker_failure: controlled worker failure for main workflow fail-closed coverage." },
                ExecutionMode: "FakeFailure",
                RealCadExecuted: false,
                RealCadConnected: false,
                PreflightReport: null));
        }

        public Task<WorkerOutput> ExecuteAsync(WorkerInput input) =>
            ExecuteAsync(input, CancellationToken.None);

        public async Task<WorkerOutput> ExecuteAsync(WorkerInput input, CancellationToken cancellationToken)
        {
            if (input.Payload is not SolidWorksWorkerRequest request)
            {
                return new WorkerOutput(
                    WorkerOutputStatus.Rejected,
                    Array.Empty<ArtifactInfo>(),
                    "FailingSolidWorksWorker requires SolidWorksWorkerRequest payload.",
                    new[] { "payload must be SolidWorksWorkerRequest." });
            }

            var result = await ExecuteAsync(request, cancellationToken);
            return new WorkerOutput(
                WorkerOutputStatus.Rejected,
                Array.Empty<ArtifactInfo>(),
                string.Join(Environment.NewLine, result.Logs),
                result.Issues);
        }
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (key, value) in values)
            {
                _previousValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
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
