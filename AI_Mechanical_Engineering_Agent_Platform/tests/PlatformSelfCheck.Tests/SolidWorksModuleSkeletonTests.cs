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
                Assert.Matches(
                    "^plate_basic_4holes_[0-9]{8}_[0-9]{6}_[0-9]{3}_[0-9a-f]{32}$",
                    Path.GetFileName(directory)));
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
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteWithApplicationAttempts++;

            if (!_connectsSuccessfully)
            {
                throw new InvalidOperationException("solidworks_application_missing: active SolidWorks COM session is not connected.");
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
            await SolidWorksPlateBuildReportWriter.WriteAsync(
                reportPath,
                request,
                "RealBuildPlateBasic4Holes",
                realCadExecuted: true,
                realCadConnected: true,
                solidWorksVersion,
                outputDirectory,
                new[] { partPath, stepPath },
                new[] { "Create part", "Save SLDPRT", "Export STEP" },
                Array.Empty<string>(),
                Array.Empty<string>(),
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
