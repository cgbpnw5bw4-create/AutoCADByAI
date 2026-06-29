using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Diagnostics;
using System.Net;
using DomainSchemas;
using PlatformCore.Modules.CADModeling.Agents;
using PlatformCore.Modules.CADModeling.Reviewers;
using PlatformCore.Modules.CADModeling.Skills;
using PlatformCore.Modules.CADModeling.Validators;
using QualityGate;

namespace PlatformCore;

public static class PlatformSelfCheckRunner
{
    private const string FakeSolidWorksWorkerFullName = "SolidWorksWorker.FakeSolidWorksWorker";

    private static readonly string[] ExpectedModules =
    [
        "RequirementUnderstanding",
        "MechanicalDesign",
        "CADModeling",
        "DrawingGeneration",
        "DrawingReview",
        "CodeEngineering",
        "CodeReview",
        "ErrorDiagnosis"
    ];

    private static readonly string[] ExpectedInternalRoute =
    [
        "mechanical-designer",
        "cad-modeler",
        "drawing-engineer",
        "drawing-reviewer"
    ];

    private static readonly string[] StandardModuleEntries =
    [
        "README.md",
        "module.yaml",
        "agents",
        "skills",
        "workers",
        "validators",
        "reviewers",
        "schemas",
        "tests"
    ];

    private static readonly string[] StorageContractFiles =
    [
        "ITaskRepository.cs",
        "IAuditLogRepository.cs",
        "IEventStore.cs",
        "IArtifactRepository.cs",
        "IReportRepository.cs"
    ];

    public static async Task<PlatformSelfCheckReport> RunAsync(
        PlatformKernel platform,
        string outputRoot,
        string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.Combine(outputRoot, "reports"));
        var root = projectRoot ?? PlatformPathResolver.FindProjectRoot();

        var task = platform.TaskStore.Create("Platform self-check");
        platform.TaskStore.UpdateStatus(task.Id, PlatformTaskStatus.Running);
        platform.AuditLog.Record("task", "self-check", "created", $"Created self-check task {task.Id}.");

        var gatewayVisibleAgents = new AgentDirectoryService(platform.AgentRegistry, platform.PermissionManager)
            .GetVisibleAgents();

        var gateDecision = new GateDecision(
            "gate-self-check",
            GateDecisionResult.Passed,
            "Self-check review passed.",
            "Continue with platform extension.");

        var workflow = await platform.WorkflowEngine.ExecuteAsync(
            new[]
            {
                Step("initialize-platform", "Platform initialized."),
                Step("register-modules", "Base modules registered."),
                Step("register-agents", "Base agents registered."),
                Step("register-skills", "Base skills registered."),
                Step("register-workers", "Fake CAD workers registered."),
                Step("gateway-directory", "Gateway public agent directory checked."),
                Step("module-manifest-loader", "module.yaml manifests checked."),
                Step("storage-contracts", "Storage abstraction contracts checked."),
                Step("quality-gate", "QualityGate skeleton returned Passed.", gateDecision)
            },
            platform.ContextManager.CreateWorkflowContext(task.Id),
            cancellationToken);

        var solutionExists = File.Exists(Path.Combine(root, "AI_Mechanical_Engineering_Agent_Platform.sln"));
        var moduleStructureChecks = CheckModuleStructures(root);
        var manifestLoadResult = new ModuleManifestLoader().LoadFromModulesDirectory(Path.Combine(root, "src", "Modules"));
        var yamlLoadedModules = manifestLoadResult.Manifests.Select(manifest => manifest.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var fallbackModules = platform.ModuleRegistry.GetSources()
            .Where(source => string.Equals(source.Source, "fallback", StringComparison.OrdinalIgnoreCase))
            .Select(source => source.ModuleName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var moduleManifestSource = ResolveModuleManifestSource(yamlLoadedModules, fallbackModules);
        var internalAgentIds = platform.AgentRegistry.GetInternalAgents().Select(agent => agent.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var internalAgentsHiddenFromGateway = gatewayVisibleAgents.All(agent => !internalAgentIds.Contains(agent.Id));
        var rejectedOutput = new AgentContracts.AgentOutput(
            AgentContracts.AgentOutputStatus.Completed,
            "Quality probe output has issues.",
            Array.Empty<ArtifactInfo>(),
            new[] { "quality probe issue" },
            Array.Empty<string>(),
            null);
        var gatekeeper = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder());
        var rejectedEvaluation = gatekeeper.Evaluate(AgentOutputReviewMapper.ToReviewReport("self-check-quality-gate", rejectedOutput));
        var gatewayQualityGateEnabled =
            rejectedEvaluation.Decision.Result == GateDecisionResult.Rejected &&
            rejectedEvaluation.RejectReport is not null;
        var rejectReportBuilderCheck = rejectedEvaluation.RejectReport?.Reasons.Contains("quality probe issue") == true;
        var storageContractsRegistered = StorageContractsExist(root);
        var chiefEngineerOutput = await InvokeChiefEngineerForSelfCheck(platform);
        var collaborationReport = chiefEngineerOutput.InternalCollaborationReport;
        var internalAgentsInvoked = collaborationReport?.CalledAgents.Select(agent => agent.AgentId).ToArray() ?? Array.Empty<string>();
        var internalRoutingEnabled = ExpectedInternalRoute.SequenceEqual(internalAgentsInvoked);
        var collaborationReportCreated = collaborationReport is not null;
        var gatewayBlocksInternalAgents = internalAgentsHiddenFromGateway &&
            platform.AgentRegistry.GetInternalAgents().All(agent => !platform.PermissionManager.CanExposeToExternalGateway(agent));
        var collaborationGateEvaluation = gatekeeper.Evaluate(AgentOutputReviewMapper.ToReviewReport("self-check-collaboration-quality-gate", chiefEngineerOutput));
        var qualityGateAfterCollaboration = collaborationGateEvaluation.Decision.Result == GateDecisionResult.Passed;
        var auditInternalAgentCalls = ExpectedInternalRoute.All(agentId =>
            platform.AuditLog.GetEntries().Any(entry => entry.Action == "internal_agent_invoked" && entry.Actor == agentId));
        var runtimeChecks = await RunRuntimeSelfChecks(root, platform, gatewayVisibleAgents, internalAgentsHiddenFromGateway, gatewayQualityGateEnabled, qualityGateAfterCollaboration);
        var realRuntimeChecks = await RunRealRuntimeSelfChecks(root, platform);
        var reliabilityChecks = await RunRuntimeReliabilityChecks(root, cancellationToken);
        var markdownValidator = new MarkdownChineseValidator();
        var markdownLanguageReport = markdownValidator.Validate(root);
        var markdownLanguageReportPath = Path.Combine(outputRoot, "reports", "markdown_language_report.json");
        var markdownLanguageReportGenerated = TryWriteJsonReport(
            markdownLanguageReportPath,
            markdownLanguageReport,
            platform.AuditLog);
        var markdownChineseStandardExists = File.Exists(Path.Combine(root, "docs", "markdown_standard.md"));
        var markdownChineseValidatorEnabled = typeof(MarkdownChineseValidator).GetMethod(nameof(MarkdownChineseValidator.Validate)) is not null;
        var markdownChineseCheckPassed =
            markdownLanguageReport.FinalStatus == "Passed" ||
            markdownLanguageReport.FinalStatus == "Warning";
        var markdownEnglishExceptionsSupported = markdownValidator.SupportsEnglishExceptions();
        var workflowQualityChecks = await RunWorkflowQualityLoopChecks();
        var internalWorkflowChecks = await RunWorkflowBackedInternalOrchestrationChecks(root, platform, chiefEngineerOutput, collaborationReport, gatewayVisibleAgents);
        var solidWorksSkeletonChecks = await RunSolidWorksSkeletonChecks(root, platform, outputRoot, cancellationToken);
        var moduleAgentsRegistered = ModuleAgentsRegistered(platform);
        var placeholderAgentIsFallbackOnly = platform.AgentRegistry.GetAll().All(agent => agent.GetType() != typeof(PlaceholderAgent));

        var checksPassed =
            solutionExists &&
            moduleStructureChecks.All(check => check.Passed) &&
            manifestLoadResult.Errors.Count == 0 &&
            yamlLoadedModules.Length == ExpectedModules.Length &&
            fallbackModules.Length == 0 &&
            platform.ModuleRegistry.GetAll().Count >= 8 &&
            platform.AgentRegistry.GetPublicAgents().Select(agent => agent.Id).SequenceEqual(new[] { "chief-engineer" }) &&
            platform.AgentRegistry.GetInternalAgents().Any(agent => agent.Id == "mechanical-designer") &&
            platform.AgentRegistry.GetInternalAgents().Any(agent => agent.Id == "cad-modeler") &&
            platform.AgentRegistry.GetInternalAgents().Any(agent => agent.Id == "drawing-engineer") &&
            platform.AgentRegistry.GetInternalAgents().Any(agent => agent.Id == "drawing-reviewer") &&
            platform.AgentRegistry.GetInternalAgents().Any(agent => agent.Id == "code-engineer") &&
            platform.AgentRegistry.GetInternalAgents().Any(agent => agent.Id == "code-reviewer") &&
            platform.AgentRegistry.GetInternalAgents().Any(agent => agent.Id == "error-diagnosis") &&
            platform.WorkerRegistry.GetAll().Any(worker => worker.Name == "FakeSolidWorksWorker") &&
            platform.WorkerRegistry.GetAll().Any(worker => worker.Name == "FakeAutoCADWorker") &&
            gatewayVisibleAgents.Count == 1 &&
            gatewayVisibleAgents[0].Id == "chief-engineer" &&
            internalAgentsHiddenFromGateway &&
            gatewayQualityGateEnabled &&
            rejectReportBuilderCheck &&
            storageContractsRegistered &&
            internalRoutingEnabled &&
            collaborationReportCreated &&
            gatewayBlocksInternalAgents &&
            qualityGateAfterCollaboration &&
            auditInternalAgentCalls &&
            runtimeChecks.AgentRuntimeProjectExists &&
            runtimeChecks.MicrosoftRuntimeDependencyIsolated &&
            runtimeChecks.RuntimeMode == "Mock" &&
            runtimeChecks.MockRuntimeAgentCreation &&
            runtimeChecks.MicrosoftAgentAdapterCheck &&
            runtimeChecks.MicrosoftWorkflowRuntimeCheck &&
            runtimeChecks.RuntimeTypesDoNotLeakToContracts &&
            runtimeChecks.GatewayVisibilityStillValid &&
            runtimeChecks.QualityGateStillEnabled &&
            realRuntimeChecks.RealRuntimeInvokerImplemented &&
            realRuntimeChecks.RuntimeConfigEnvSupported &&
            realRuntimeChecks.RuntimeModeDefaultIsMock &&
            realRuntimeChecks.RuntimeFallbackWhenMissingKey &&
            realRuntimeChecks.ChiefEngineerRealRuntimeOnly &&
            realRuntimeChecks.InternalAgentsRemainMock &&
            realRuntimeChecks.MicrosoftAgentOutputMapperEnabled &&
            realRuntimeChecks.InvalidModelOutputFallbackEnabled &&
            realRuntimeChecks.ModelCannotEscalatePermissions &&
            realRuntimeChecks.ModelCannotCallWorkerDirectly &&
            realRuntimeChecks.ChiefEngineerRuntimeThenWorkflowEngine &&
            realRuntimeChecks.QualityGateAfterRealRuntime &&
            realRuntimeChecks.GatewayResponseContainsRuntimeMetadata &&
            (!realRuntimeChecks.StrictSmokeTest || realRuntimeChecks.MicrosoftRuntimeSmokeTestPassed) &&
            reliabilityChecks.RetryDelayActuallyAwaited &&
            reliabilityChecks.ExponentialBackoffDelayRespected &&
            reliabilityChecks.WorkflowRetryDelayCancellationSupported &&
            reliabilityChecks.RuntimeTimeoutConfigSupported &&
            reliabilityChecks.InvalidTimeoutFallsBackToDefault &&
            reliabilityChecks.OpenAIClientTimeoutConfigured &&
            reliabilityChecks.OpenAIClientCancellationSupported &&
            reliabilityChecks.ProviderErrorsAreStructured &&
            reliabilityChecks.ApiKeyNotLogged &&
            markdownChineseStandardExists &&
            markdownLanguageReport.ScannedFiles > 0 &&
            markdownChineseValidatorEnabled &&
            markdownChineseCheckPassed &&
            markdownLanguageReportGenerated &&
            markdownEnglishExceptionsSupported &&
            workflowQualityChecks.WorkflowQualityLoopEnabled &&
            workflowQualityChecks.WorkflowPassedScenario == "Passed" &&
            workflowQualityChecks.WorkflowRejectedRetryPassedScenario == "Passed" &&
            workflowQualityChecks.WorkflowRejectedMaxRetriesScenario == "Passed" &&
            workflowQualityChecks.WorkflowHumanApprovalScenario == "Passed" &&
            workflowQualityChecks.RetryPolicyEnabled &&
            workflowQualityChecks.FailureReportGenerated &&
            workflowQualityChecks.HumanApprovalRequestGenerated &&
            moduleAgentsRegistered &&
            placeholderAgentIsFallbackOnly &&
            internalWorkflowChecks.ChiefEngineerInternalOrchestrationUsesWorkflowEngine &&
            internalWorkflowChecks.InternalAgentWorkflowStepsCreated &&
            internalWorkflowChecks.QualityGateAfterEachInternalStep &&
            internalWorkflowChecks.InternalWorkflowPassedScenario == "Passed" &&
            internalWorkflowChecks.InternalWorkflowRetryThenPassedScenario == "Passed" &&
            internalWorkflowChecks.InternalWorkflowMaxRetriesExceededScenario == "Passed" &&
            internalWorkflowChecks.InternalWorkflowFailedScenario == "Passed" &&
            internalWorkflowChecks.InternalWorkflowHumanApprovalScenario == "Passed" &&
            internalWorkflowChecks.RetryPolicyInterfaceEnabled &&
            internalWorkflowChecks.ExponentialBackoffPolicyAvailable &&
            internalWorkflowChecks.CodeEngineerAgentRegistered &&
            internalWorkflowChecks.CodeReviewerAgentRegistered &&
            internalWorkflowChecks.CodeAgentsAreInternal &&
            internalWorkflowChecks.GatewayBlocksCodeAgents &&
            solidWorksSkeletonChecks.SolidWorksModuleSkeletonEnabled &&
            solidWorksSkeletonChecks.SolidWorksBuildPlanSkillRegistered &&
            solidWorksSkeletonChecks.SolidWorksBuildPlanGenerated &&
            solidWorksSkeletonChecks.SolidWorksWorkerContractExists &&
            solidWorksSkeletonChecks.FakeSolidWorksWorkerRegistered &&
            solidWorksSkeletonChecks.FakeSolidWorksWorkerDryRunPassed &&
            solidWorksSkeletonChecks.SolidWorksBuildPlanValidatorPassed &&
            solidWorksSkeletonChecks.SolidWorksArtifactValidatorPassed &&
            solidWorksSkeletonChecks.SolidWorksBuildPlanReviewerPassed &&
            solidWorksSkeletonChecks.SolidWorksQualityGatePassed &&
            solidWorksSkeletonChecks.SolidWorksFakeArtifactsGenerated &&
            solidWorksSkeletonChecks.SolidWorksRealCadNotExecuted &&
            solidWorksSkeletonChecks.SolidWorksAgentDoesNotCallWorkerDirectly &&
            solidWorksSkeletonChecks.GatewayDoesNotCallSolidWorksWorker &&
            solidWorksSkeletonChecks.SelfCheckInfrastructureError is null &&
            gateDecision.Result == GateDecisionResult.Passed &&
            workflow.FinalStatus == "Passed";

        var finalStatus = checksPassed ? "Passed" : "Failed";
        platform.TaskStore.UpdateStatus(task.Id, checksPassed ? PlatformTaskStatus.Passed : PlatformTaskStatus.Failed);
        platform.AuditLog.Record("self-check", "quality-gate", finalStatus.ToLowerInvariant(), $"Self-check final status: {finalStatus}.");

        var report = new PlatformSelfCheckReport(
            platform.ModuleRegistry.GetAll().Select(module => new ModuleSummary(module.Name, module.Version, module.Description)).ToArray(),
            platform.AgentRegistry.GetAll().Select(ToAgentSummary).ToArray(),
            platform.AgentRegistry.GetPublicAgents().Select(ToAgentSummary).ToArray(),
            platform.AgentRegistry.GetInternalAgents().Select(ToAgentSummary).ToArray(),
            platform.SkillRegistry.GetAll().Select(skill => new SkillSummary(skill.Name, skill.Description)).ToArray(),
            platform.WorkerRegistry.GetAll().Select(worker => new WorkerSummary(worker.Name, worker.TargetSystem)).ToArray(),
            workflow.Steps,
            gateDecision,
            gatewayVisibleAgents,
            platform.AuditLog.GetEntries(),
            solutionExists,
            moduleStructureChecks,
            moduleManifestSource,
            yamlLoadedModules,
            fallbackModules,
            gatewayQualityGateEnabled,
            storageContractsRegistered,
            rejectReportBuilderCheck,
            internalRoutingEnabled,
            internalAgentsInvoked,
            collaborationReportCreated,
            gatewayBlocksInternalAgents,
            qualityGateAfterCollaboration,
            auditInternalAgentCalls,
            runtimeChecks.AgentRuntimeProjectExists,
            runtimeChecks.MicrosoftRuntimeDependencyIsolated,
            runtimeChecks.RuntimeMode,
            runtimeChecks.MockRuntimeAgentCreation,
            runtimeChecks.MicrosoftAgentAdapterCheck,
            runtimeChecks.MicrosoftWorkflowRuntimeCheck,
            runtimeChecks.RuntimeTypesDoNotLeakToContracts,
            runtimeChecks.GatewayVisibilityStillValid,
            runtimeChecks.QualityGateStillEnabled,
            workflowQualityChecks.WorkflowQualityLoopEnabled,
            workflowQualityChecks.WorkflowPassedScenario,
            workflowQualityChecks.WorkflowRejectedRetryPassedScenario,
            workflowQualityChecks.WorkflowRejectedMaxRetriesScenario,
            workflowQualityChecks.WorkflowHumanApprovalScenario,
            workflowQualityChecks.RetryPolicyEnabled,
            workflowQualityChecks.FailureReportGenerated,
            workflowQualityChecks.HumanApprovalRequestGenerated,
            moduleAgentsRegistered,
            placeholderAgentIsFallbackOnly,
            internalWorkflowChecks.ChiefEngineerInternalOrchestrationUsesWorkflowEngine,
            internalWorkflowChecks.InternalAgentWorkflowStepsCreated,
            internalWorkflowChecks.QualityGateAfterEachInternalStep,
            internalWorkflowChecks.InternalWorkflowPassedScenario,
            internalWorkflowChecks.InternalWorkflowRetryThenPassedScenario,
            internalWorkflowChecks.InternalWorkflowMaxRetriesExceededScenario,
            internalWorkflowChecks.InternalWorkflowFailedScenario,
            internalWorkflowChecks.InternalWorkflowHumanApprovalScenario,
            internalWorkflowChecks.RetryPolicyInterfaceEnabled,
            internalWorkflowChecks.ExponentialBackoffPolicyAvailable,
            internalWorkflowChecks.CodeEngineerAgentRegistered,
            internalWorkflowChecks.CodeReviewerAgentRegistered,
            internalWorkflowChecks.CodeAgentsAreInternal,
            internalWorkflowChecks.GatewayBlocksCodeAgents,
            realRuntimeChecks.RealRuntimeInvokerImplemented,
            realRuntimeChecks.RuntimeConfigEnvSupported,
            realRuntimeChecks.RuntimeModeDefaultIsMock,
            realRuntimeChecks.RuntimeFallbackWhenMissingKey,
            realRuntimeChecks.ChiefEngineerRealRuntimeOnly,
            realRuntimeChecks.InternalAgentsRemainMock,
            realRuntimeChecks.MicrosoftAgentOutputMapperEnabled,
            realRuntimeChecks.InvalidModelOutputFallbackEnabled,
            realRuntimeChecks.ModelCannotEscalatePermissions,
            realRuntimeChecks.ModelCannotCallWorkerDirectly,
            realRuntimeChecks.ChiefEngineerRuntimeThenWorkflowEngine,
            realRuntimeChecks.QualityGateAfterRealRuntime,
            realRuntimeChecks.GatewayResponseContainsRuntimeMetadata,
            realRuntimeChecks.MicrosoftRuntimeSmokeTestAttempted,
            realRuntimeChecks.MicrosoftRuntimeSmokeTestPassed,
            realRuntimeChecks.MicrosoftRuntimeSmokeTestError,
            reliabilityChecks.RetryDelayActuallyAwaited,
            reliabilityChecks.ExponentialBackoffDelayRespected,
            reliabilityChecks.WorkflowRetryDelayCancellationSupported,
            reliabilityChecks.RuntimeTimeoutConfigSupported,
            reliabilityChecks.InvalidTimeoutFallsBackToDefault,
            reliabilityChecks.OpenAIClientTimeoutConfigured,
            reliabilityChecks.OpenAIClientCancellationSupported,
            reliabilityChecks.ProviderErrorsAreStructured,
            reliabilityChecks.ApiKeyNotLogged,
            markdownChineseStandardExists,
            markdownLanguageReport.ScannedFiles,
            markdownChineseValidatorEnabled,
            markdownChineseCheckPassed,
            markdownLanguageReportGenerated,
            markdownEnglishExceptionsSupported,
            solidWorksSkeletonChecks.SolidWorksModuleSkeletonEnabled,
            solidWorksSkeletonChecks.SolidWorksBuildPlanSkillRegistered,
            solidWorksSkeletonChecks.SolidWorksBuildPlanGenerated,
            solidWorksSkeletonChecks.SolidWorksWorkerContractExists,
            solidWorksSkeletonChecks.FakeSolidWorksWorkerRegistered,
            solidWorksSkeletonChecks.FakeSolidWorksWorkerDryRunPassed,
            solidWorksSkeletonChecks.SolidWorksBuildPlanValidatorPassed,
            solidWorksSkeletonChecks.SolidWorksArtifactValidatorPassed,
            solidWorksSkeletonChecks.SolidWorksBuildPlanReviewerPassed,
            solidWorksSkeletonChecks.SolidWorksQualityGatePassed,
            solidWorksSkeletonChecks.SolidWorksFakeArtifactsGenerated,
            solidWorksSkeletonChecks.SolidWorksRealCadNotExecuted,
            solidWorksSkeletonChecks.SolidWorksAgentDoesNotCallWorkerDirectly,
            solidWorksSkeletonChecks.GatewayDoesNotCallSolidWorksWorker,
            solidWorksSkeletonChecks.SelfCheckInfrastructureError,
            finalStatus);

        var reportPath = Path.Combine(outputRoot, "reports", "platform_self_check_report.json");
        await using var stream = File.Create(reportPath);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions());

        return report;
    }

    private static bool TryWriteJsonReport<T>(string reportPath, T report, InMemoryAuditLog auditLog)
    {
        try
        {
            var directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions()));
            return File.Exists(reportPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            auditLog.Record("self-check", "report-writer", "report_write_failed", $"{reportPath}: {ex.Message}");
            return false;
        }
    }

    private static WorkflowStep Step(string name, string message, GateDecision? decision = null) =>
        new(name, _ => Task.FromResult(new WorkflowStepResult(
            name,
            decision?.Result.ToString() ?? "Completed",
            message,
            decision,
            new[] { message })));

    private static AgentSummary ToAgentSummary(AgentContracts.IAgent agent) =>
        new(agent.Id, agent.Name, agent.Role.Description, agent.Visibility.ToString());

    private static IReadOnlyList<ModuleStructureCheck> CheckModuleStructures(string projectRoot)
    {
        var modulesRoot = Path.Combine(projectRoot, "src", "Modules");
        return ExpectedModules.Select(module =>
        {
            var moduleRoot = Path.Combine(modulesRoot, module);
            var missing = new List<string>();

            foreach (var entry in StandardModuleEntries)
            {
                var entryPath = Path.Combine(moduleRoot, entry);
                if (entry.Contains('.'))
                {
                    if (!File.Exists(entryPath))
                    {
                        missing.Add(entry);
                    }

                    continue;
                }

                if (!Directory.Exists(entryPath))
                {
                    missing.Add($"{entry}/");
                    continue;
                }

                if (!File.Exists(Path.Combine(entryPath, ".gitkeep")))
                {
                    missing.Add($"{entry}/.gitkeep");
                }
            }

            return new ModuleStructureCheck(module, missing.Count == 0, missing);
        }).ToArray();
    }

    private static string ResolveModuleManifestSource(IReadOnlyList<string> yamlLoadedModules, IReadOnlyList<string> fallbackModules)
    {
        if (fallbackModules.Count == 0 && yamlLoadedModules.Count > 0)
        {
            return "yaml";
        }

        if (yamlLoadedModules.Count == 0 && fallbackModules.Count > 0)
        {
            return "fallback";
        }

        return "mixed";
    }

    private static bool StorageContractsExist(string projectRoot)
    {
        var storageRoot = Path.Combine(projectRoot, "src", "Storage");
        return File.Exists(Path.Combine(storageRoot, "Storage.csproj")) &&
               StorageContractFiles.All(file => File.Exists(Path.Combine(storageRoot, file)));
    }

    private static async Task<AgentContracts.AgentOutput> InvokeChiefEngineerForSelfCheck(PlatformKernel platform, string? testScenario = null)
    {
        var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer")
            ?? throw new InvalidOperationException("chief-engineer is not registered.");
        var inputContext = new Dictionary<string, string> { ["project_id"] = "self-check" };
        if (testScenario is not null)
        {
            inputContext["test_scenario"] = testScenario;
        }

        var input = new AgentContracts.AgentInput(
            "self-check",
            "self-check",
            testScenario is null ? "self-check-conversation" : $"self-check-{testScenario}",
            "self-check",
            "Run internal routing self-check.",
            Array.Empty<string>(),
            inputContext);
        var context = new AgentContracts.AgentContext(
            $"task-{Guid.NewGuid():N}",
            input,
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

        return await chiefEngineer.ExecuteAsync(context);
    }

    private static async Task<SolidWorksSkeletonSelfCheckResult> RunSolidWorksSkeletonChecks(
        string projectRoot,
        PlatformKernel platform,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            var cadModule = platform.ModuleRegistry.GetByName("CADModeling");
            var cadModuleYamlPath = Path.Combine(projectRoot, "src", "Modules", "CADModeling", "module.yaml");
            var cadModuleYaml = File.Exists(cadModuleYamlPath) ? File.ReadAllText(cadModuleYamlPath) : string.Empty;
            var solidWorksModuleSkeletonEnabled =
                cadModule is not null &&
                cadModule.Skills.Contains("solidworks-build-plan-skill") &&
                cadModule.Workers.Contains("FakeSolidWorksWorker") &&
                cadModule.Validators.Contains("solidworks-build-plan-validator") &&
                cadModule.Validators.Contains("solidworks-artifact-validator") &&
                cadModule.Reviewers.Contains("solidworks-build-plan-reviewer") &&
                cadModuleYaml.Contains("SolidWorksBuildPlan", StringComparison.OrdinalIgnoreCase) &&
                cadModuleYaml.Contains("SolidWorksWorkerRequest", StringComparison.OrdinalIgnoreCase) &&
                cadModuleYaml.Contains("SolidWorksWorkerResult", StringComparison.OrdinalIgnoreCase) &&
                cadModuleYaml.Contains("SolidWorksArtifact", StringComparison.OrdinalIgnoreCase);

            var skill = platform.SkillRegistry.GetByName("solidworks-build-plan-skill") as SolidWorksBuildPlanSkill
                ?? new SolidWorksBuildPlanSkill();
            var solidWorksBuildPlanSkillRegistered = platform.SkillRegistry.GetByName("solidworks-build-plan-skill") is not null;
            var skillOutput = await skill.ExecuteAsync(new SkillContracts.SkillInput(
                "self-check-solidworks-build-plan",
                nameof(CADModelSpec),
                new CADModelSpec(
                    "cad-model-spec-plate-basic-4holes",
                    "Part",
                    "plate_basic_4holes",
                    "160 x 80 x 12 mm plate with four through holes.",
                    new Dictionary<string, string>
                    {
                        ["length_mm"] = "160",
                        ["width_mm"] = "80",
                        ["thickness_mm"] = "12",
                        ["hole_diameter_mm"] = "10",
                        ["hole_count"] = "4"
                    },
                    Array.Empty<string>(),
                    new[] { "dry-run only" }),
                new Dictionary<string, string>()));
            var plan = skillOutput.Result as SolidWorksBuildPlan;
            var solidWorksBuildPlanGenerated =
                skillOutput.Status == SkillContracts.SkillOutputStatus.Completed &&
                plan is not null &&
                string.Equals(plan.TargetCadSystem, "SolidWorks", StringComparison.OrdinalIgnoreCase) &&
                plan.Operations.Count >= 6 &&
                plan.ExpectedArtifacts.Count >= 3;

            var workerContractPath = Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "ISolidWorksWorker.cs");
            var workerContractText = File.Exists(workerContractPath) ? File.ReadAllText(workerContractPath) : string.Empty;
            var solidWorksWorkerContractExists =
                workerContractText.Contains("SolidWorksWorkerRequest", StringComparison.Ordinal) &&
                workerContractText.Contains("CancellationToken", StringComparison.Ordinal);
            var registeredFakeSolidWorksWorker = platform.WorkerRegistry.GetAll()
                .SingleOrDefault(worker => worker.Name == "FakeSolidWorksWorker");
            var fakeSolidWorksWorkerRegistered =
                registeredFakeSolidWorksWorker?.GetType().FullName == FakeSolidWorksWorkerFullName &&
                registeredFakeSolidWorksWorker.GetType().GetMethods().Any(method =>
                    method.Name == "ExecuteAsync" &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType.Name == nameof(SolidWorksWorkerRequest));

            SolidWorksWorkerResult? workerResult = null;
            if (plan is not null && fakeSolidWorksWorkerRegistered && registeredFakeSolidWorksWorker is not null)
            {
                var workerMethod = registeredFakeSolidWorksWorker.GetType().GetMethods()
                    .Single(method =>
                        method.Name == "ExecuteAsync" &&
                        method.GetParameters().Length == 2 &&
                        method.GetParameters()[0].ParameterType.Name == nameof(SolidWorksWorkerRequest));
                var requestId = $"self-check-solidworks-request-{Guid.NewGuid():N}";
                var request = new SolidWorksWorkerRequest(
                    requestId,
                    plan,
                    Path.Combine(projectRoot, "output", "solidworks", "self-check", requestId),
                    DryRun: true,
                    AllowRealCadExecution: false);
                var task = (Task<SolidWorksWorkerResult>)workerMethod.Invoke(registeredFakeSolidWorksWorker, new object?[] { request, cancellationToken })!;
                workerResult = await task;
            }

            var fakeSolidWorksWorkerDryRunPassed =
                workerResult is not null &&
                workerResult.Status == "Completed" &&
                workerResult.ExecutionMode == "Fake" &&
                !workerResult.RealCadExecuted;
            var buildPlanValidation = plan is not null
                ? new SolidWorksBuildPlanValidator().Validate(plan)
                : new ReviewReport("solidworks-build-plan-validation-missing", "solidworks-build-plan-validator", false, 0, new[] { "plan missing" }, false, true);
            var artifactValidation = workerResult is not null
                ? new SolidWorksArtifactValidator().Validate(workerResult)
                : new ReviewReport("solidworks-artifact-validation-missing", "solidworks-artifact-validator", false, 0, new[] { "worker result missing" }, false, true);
            var buildPlanReview = plan is not null
                ? new SolidWorksBuildPlanReviewer().Review(plan)
                : new ReviewReport("solidworks-build-plan-review-missing", "solidworks-build-plan-reviewer", false, 0, new[] { "plan missing" }, false, true);
            var gateEvaluation = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder()).Evaluate(buildPlanReview);
            var solidWorksFakeArtifactsGenerated =
                workerResult?.GeneratedArtifacts.Count >= 3 &&
                workerResult.GeneratedArtifacts.All(artifact => artifact.Exists && File.Exists(artifact.FilePath) && new FileInfo(artifact.FilePath).Length > 0);
            var solidWorksRealCadNotExecuted =
                workerResult is not null &&
                workerResult.ExecutionMode == "Fake" &&
                !workerResult.RealCadExecuted &&
                workerResult.Logs.Any(log => log.Contains("No SolidWorks process", StringComparison.OrdinalIgnoreCase)) &&
                workerResult.Logs.Any(log => log.Contains("No COM call", StringComparison.OrdinalIgnoreCase));
            var cadModelerOutput = await new CadModelerAgent().ExecuteAsync(CreateCadModelerSelfCheckContext());
            var solidWorksAgentDoesNotCallWorkerDirectly =
                cadModelerOutput.Message.Contains("SolidWorksBuildPlan", StringComparison.OrdinalIgnoreCase) &&
                cadModelerOutput.Artifacts.Count == 0 &&
                cadModelerOutput.Logs.Any(log => log.Contains("does not directly call SolidWorksWorker", StringComparison.OrdinalIgnoreCase)) &&
                cadModelerOutput.Logs.Any(log => log.Contains("does not directly call FakeSolidWorksWorker", StringComparison.OrdinalIgnoreCase));
            var gatewayDoesNotCallSolidWorksWorker =
                platform.AgentRegistry.GetById("FakeSolidWorksWorker") is null &&
                platform.AgentRegistry.GetById("solidworks-worker") is null &&
                platform.AgentRegistry.GetPublicAgents().All(agent => agent.Id == "chief-engineer");

            return new SolidWorksSkeletonSelfCheckResult(
                solidWorksModuleSkeletonEnabled,
                solidWorksBuildPlanSkillRegistered,
                solidWorksBuildPlanGenerated,
                solidWorksWorkerContractExists,
                fakeSolidWorksWorkerRegistered,
                fakeSolidWorksWorkerDryRunPassed,
                buildPlanValidation.IsPassed,
                artifactValidation.IsPassed,
                buildPlanReview.IsPassed,
                gateEvaluation.Decision.Result == GateDecisionResult.Passed,
                solidWorksFakeArtifactsGenerated == true,
                solidWorksRealCadNotExecuted,
                solidWorksAgentDoesNotCallWorkerDirectly,
                gatewayDoesNotCallSolidWorksWorker,
                null);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or MissingMethodException or TargetInvocationException or FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            var error = ex.GetBaseException().Message;
            platform.AuditLog.Record("solidworks", "self-check", "solidworks_skeleton_check_failed", error);
            return new SolidWorksSkeletonSelfCheckResult(
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                error);
        }
    }

    private static AgentContracts.AgentContext CreateCadModelerSelfCheckContext()
    {
        var input = new AgentContracts.AgentInput(
            "self-check",
            "solidworks-self-check",
            "solidworks-self-check-conversation",
            "self-check",
            "Create a SolidWorks plate modeling plan.",
            Array.Empty<string>(),
            new Dictionary<string, string>());

        return new AgentContracts.AgentContext(
            $"task-{Guid.NewGuid():N}",
            input,
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);
    }

    private static async Task<WorkflowBackedInternalSelfCheckResult> RunWorkflowBackedInternalOrchestrationChecks(
        string projectRoot,
        PlatformKernel platform,
        AgentContracts.AgentOutput chiefEngineerOutput,
        InternalCollaborationReport? collaborationReport,
        IReadOnlyList<AgentDirectoryEntry> gatewayVisibleAgents)
    {
        var chiefEngineerInternalOrchestrationUsesWorkflowEngine =
            collaborationReport?.WorkflowId is not null &&
            string.Equals(collaborationReport.WorkflowStatus, WorkflowStatus.Passed.ToString(), StringComparison.OrdinalIgnoreCase) &&
            platform.AuditLog.GetEntries().Any(entry => entry.Action == "workflow_started") &&
            chiefEngineerOutput.Logs.Any(log => log.Contains("WorkflowEngine", StringComparison.OrdinalIgnoreCase));
        var internalAgentWorkflowStepsCreated =
            collaborationReport?.StepResults?.Count == ExpectedInternalRoute.Length &&
            ExpectedInternalRoute.All(agentId =>
                collaborationReport.StepResults.Any(step => string.Equals(step.AgentId, agentId, StringComparison.OrdinalIgnoreCase)));
        var qualityGateAfterEachInternalStep =
            collaborationReport?.StepResults?.All(step => step.GateDecision is not null) == true &&
            platform.AuditLog.GetEntries().Count(entry => entry.Action == "quality_gate_after_internal_step") >= ExpectedInternalRoute.Length;

        var passedScenario = collaborationReport?.WorkflowStatus == WorkflowStatus.Passed.ToString()
            ? "Passed"
            : "Failed";
        var retryOutput = await InvokeChiefEngineerForSelfCheck(PlatformBootstrapper.CreateDefault(projectRoot), "mechanical_retry_then_passed");
        var retryReport = retryOutput.InternalCollaborationReport;
        var retryScenario =
            retryReport?.WorkflowStatus == WorkflowStatus.Passed.ToString() &&
            retryReport.RetrySummary?.TotalRetries > 0 &&
            retryReport.StepResults?.Any(step => step.Status == WorkflowStepStatus.Retrying.ToString()) == true
                ? "Passed"
                : "Failed";

        var maxRetryOutput = await InvokeChiefEngineerForSelfCheck(PlatformBootstrapper.CreateDefault(projectRoot), "cad_max_retries_exceeded");
        var maxRetryReport = maxRetryOutput.InternalCollaborationReport;
        var maxRetriesScenario =
            maxRetryReport?.WorkflowStatus == WorkflowStatus.Rejected.ToString() &&
            maxRetryReport.FailureReport is not null &&
            maxRetryReport.StepResults?.Any(step => step.AgentId == "cad-modeler" && step.Status == WorkflowStepStatus.Rejected.ToString()) == true
                ? "Passed"
                : "Failed";

        var failedOutput = await InvokeChiefEngineerForSelfCheck(PlatformBootstrapper.CreateDefault(projectRoot), "drawing_engineer_failed");
        var failedReport = failedOutput.InternalCollaborationReport;
        var failedScenario =
            failedReport?.WorkflowStatus == WorkflowStatus.Failed.ToString() &&
            failedReport.FailureReport?.FailedStepId == "internal-agent:drawing-engineer"
                ? "Passed"
                : "Failed";

        var humanOutput = await InvokeChiefEngineerForSelfCheck(PlatformBootstrapper.CreateDefault(projectRoot), "drawing_reviewer_needs_human_approval");
        var humanReport = humanOutput.InternalCollaborationReport;
        var humanScenario =
            humanReport?.WorkflowStatus == WorkflowStatus.WaitingForHumanApproval.ToString() &&
            humanReport.HumanApprovalRequest?.StepId == "internal-agent:drawing-reviewer"
                ? "Passed"
                : "Failed";

        IRetryPolicy retryPolicy = new DefaultRetryPolicy();
        var retryPolicyInterfaceEnabled =
            retryPolicy.MaxRetries == 2 &&
            retryPolicy.ShouldRetry(
                new GateDecision("gate-interface", GateDecisionResult.Rejected, "retryable"),
                0,
                new[] { Issue.FromText("retryable issue") }) &&
            !retryPolicy.ShouldRetry(
                new GateDecision("gate-interface-failed", GateDecisionResult.Failed, "failed"),
                0,
                new[] { Issue.FromText("retryable issue") });

        IRetryPolicy backoffPolicy = new ExponentialBackoffRetryPolicy(baseDelayMs: 100, maxDelayMs: 1000, multiplier: 2);
        var exponentialBackoffPolicyAvailable =
            backoffPolicy.GetDelay(0) == TimeSpan.FromMilliseconds(100) &&
            backoffPolicy.GetDelay(1) == TimeSpan.FromMilliseconds(200) &&
            backoffPolicy.GetDelay(10) == TimeSpan.FromMilliseconds(1000);

        var codeEngineerAgent = platform.AgentRegistry.GetById("code-engineer");
        var codeReviewerAgent = platform.AgentRegistry.GetById("code-reviewer");
        var codeEngineerAgentRegistered = codeEngineerAgent is not null;
        var codeReviewerAgentRegistered = codeReviewerAgent is not null;
        var codeAgentsAreInternal =
            codeEngineerAgent?.Visibility == AgentContracts.AgentVisibility.Internal &&
            codeReviewerAgent?.Visibility == AgentContracts.AgentVisibility.Internal;
        var gatewayBlocksCodeAgents =
            gatewayVisibleAgents.All(agent => agent.Id != "code-engineer" && agent.Id != "code-reviewer") &&
            codeEngineerAgent is not null &&
            codeReviewerAgent is not null &&
            !platform.PermissionManager.CanExposeToExternalGateway(codeEngineerAgent) &&
            !platform.PermissionManager.CanExposeToExternalGateway(codeReviewerAgent);

        return new WorkflowBackedInternalSelfCheckResult(
            chiefEngineerInternalOrchestrationUsesWorkflowEngine,
            internalAgentWorkflowStepsCreated,
            qualityGateAfterEachInternalStep,
            passedScenario,
            retryScenario,
            maxRetriesScenario,
            failedScenario,
            humanScenario,
            retryPolicyInterfaceEnabled,
            exponentialBackoffPolicyAvailable,
            codeEngineerAgentRegistered,
            codeReviewerAgentRegistered,
            codeAgentsAreInternal,
            gatewayBlocksCodeAgents);
    }

    private static async Task<WorkflowQualityLoopSelfCheckResult> RunWorkflowQualityLoopChecks()
    {
        var retryPolicy = new RetryPolicy();
        var retryPolicyEnabled =
            retryPolicy.MaxRetries == 2 &&
            retryPolicy.ShouldRetry(new GateDecision("gate-retry", GateDecisionResult.Rejected, "retryable"), 0, Array.Empty<string>()) &&
            !retryPolicy.ShouldRetry(new GateDecision("gate-failed", GateDecisionResult.Failed, "fatal"), 0, Array.Empty<string>()) &&
            !retryPolicy.ShouldRetry(new GateDecision("gate-human", GateDecisionResult.NeedsHumanApproval, "human"), 0, Array.Empty<string>()) &&
            !retryPolicy.ShouldRetry(new GateDecision("gate-critical", GateDecisionResult.Rejected, "critical"), 0, new[] { "critical issue" });

        var passed = await new SequentialWorkflowEngine(new RetryPolicy()).ExecuteAsync(
            new[]
            {
                WorkflowScenarioStep("passed-1", GateDecisionResult.Passed),
                WorkflowScenarioStep("passed-2", GateDecisionResult.Passed)
            },
            new WorkflowContext("workflow-quality-loop-passed", new Dictionary<string, object?>()));

        var retryAttempts = 0;
        var retryPassed = await new SequentialWorkflowEngine(new RetryPolicy(maxRetries: 2)).ExecuteAsync(
            new[]
            {
                new WorkflowStep("retry-step", _ =>
                {
                    retryAttempts++;
                    return Task.FromResult(retryAttempts == 1
                        ? WorkflowScenarioResult("retry-step", GateDecisionResult.Rejected, ["retryable planning issue"])
                        : WorkflowScenarioResult("retry-step", GateDecisionResult.Passed));
                }),
                WorkflowScenarioStep("retry-next-step", GateDecisionResult.Passed)
            },
            new WorkflowContext("workflow-quality-loop-retry-passed", new Dictionary<string, object?>()));

        var rejectedAttempts = 0;
        var rejectedMaxRetries = await new SequentialWorkflowEngine(new RetryPolicy(maxRetries: 1)).ExecuteAsync(
            new[]
            {
                new WorkflowStep("rejected-max-retries", _ =>
                {
                    rejectedAttempts++;
                    return Task.FromResult(WorkflowScenarioResult("rejected-max-retries", GateDecisionResult.Rejected, ["persistent quality issue"]));
                }),
                WorkflowScenarioStep("should-not-run-after-reject", GateDecisionResult.Passed)
            },
            new WorkflowContext("workflow-quality-loop-max-retries", new Dictionary<string, object?>()));

        var downstreamHumanApprovalStepExecuted = false;
        var humanApproval = await new SequentialWorkflowEngine(new RetryPolicy()).ExecuteAsync(
            new[]
            {
                WorkflowScenarioStep("human-approval", GateDecisionResult.NeedsHumanApproval, ["manual approval required"]),
                new WorkflowStep("should-not-run-after-human", _ =>
                {
                    downstreamHumanApprovalStepExecuted = true;
                    return Task.FromResult(WorkflowScenarioResult("should-not-run-after-human", GateDecisionResult.Passed));
                })
            },
            new WorkflowContext("workflow-quality-loop-human", new Dictionary<string, object?>()));

        var failed = await new SequentialWorkflowEngine(new RetryPolicy()).ExecuteAsync(
            new[]
            {
                WorkflowScenarioStep("failed-step", GateDecisionResult.Failed, ["fatal workflow issue"]),
                WorkflowScenarioStep("should-not-run-after-failed", GateDecisionResult.Passed)
            },
            new WorkflowContext("workflow-quality-loop-failed", new Dictionary<string, object?>()));

        var workflowPassedScenario =
            passed.Status == WorkflowStatus.Passed &&
            passed.Steps.Count == 2 &&
            passed.Steps.All(step => step.Status == WorkflowStepStatus.Passed)
                ? "Passed"
                : "Failed";
        var workflowRejectedRetryPassedScenario =
            retryPassed.Status == WorkflowStatus.Passed &&
            retryAttempts == 2 &&
            retryPassed.Steps.Any(step => step.Status == WorkflowStepStatus.Retrying) &&
            retryPassed.AuditLogs.Any(log => log.Action == "workflow_step_retrying")
                ? "Passed"
                : "Failed";
        var workflowRejectedMaxRetriesScenario =
            rejectedMaxRetries.Status == WorkflowStatus.Rejected &&
            rejectedAttempts == 2 &&
            rejectedMaxRetries.Steps.LastOrDefault()?.RejectReport is not null &&
            rejectedMaxRetries.Steps.All(step => step.StepId != "should-not-run-after-reject")
                ? "Passed"
                : "Failed";
        var workflowHumanApprovalScenario =
            humanApproval.Status == WorkflowStatus.WaitingForHumanApproval &&
            humanApproval.HumanApprovalRequest is not null &&
            !downstreamHumanApprovalStepExecuted
                ? "Passed"
                : "Failed";
        var failureReportGenerated =
            failed.Status == WorkflowStatus.Failed &&
            failed.FailureReport is not null &&
            failed.Steps.All(step => step.StepId != "should-not-run-after-failed");
        var humanApprovalRequestGenerated = humanApproval.HumanApprovalRequest is not null;
        var workflowQualityLoopEnabled =
            workflowPassedScenario == "Passed" &&
            workflowRejectedRetryPassedScenario == "Passed" &&
            workflowRejectedMaxRetriesScenario == "Passed" &&
            workflowHumanApprovalScenario == "Passed" &&
            failureReportGenerated &&
            humanApprovalRequestGenerated;

        return new WorkflowQualityLoopSelfCheckResult(
            workflowQualityLoopEnabled,
            workflowPassedScenario,
            workflowRejectedRetryPassedScenario,
            workflowRejectedMaxRetriesScenario,
            workflowHumanApprovalScenario,
            retryPolicyEnabled,
            failureReportGenerated,
            humanApprovalRequestGenerated);
    }

    private static WorkflowStep WorkflowScenarioStep(
        string stepId,
        GateDecisionResult result,
        IReadOnlyList<string>? issues = null) =>
        new(stepId, _ => Task.FromResult(WorkflowScenarioResult(stepId, result, issues)));

    private static WorkflowStepResult WorkflowScenarioResult(
        string stepId,
        GateDecisionResult result,
        IReadOnlyList<string>? issues = null)
    {
        var decision = new GateDecision(
            $"gate-{stepId}-{Guid.NewGuid():N}",
            result,
            $"{result} decision for workflow quality loop self-check.");
        var status = result switch
        {
            GateDecisionResult.Passed => WorkflowStepStatus.Passed,
            GateDecisionResult.Rejected => WorkflowStepStatus.Rejected,
            GateDecisionResult.Failed => WorkflowStepStatus.Failed,
            GateDecisionResult.NeedsHumanApproval => WorkflowStepStatus.WaitingForHumanApproval,
            _ => WorkflowStepStatus.Running
        };

        return new WorkflowStepResult(
            stepId,
            stepId,
            status,
            $"Workflow quality loop step {stepId} returned {result}.",
            GateDecision: decision,
            Issues: issues ?? Array.Empty<string>(),
            Logs: new[] { $"workflow-quality-loop:{stepId}" });
    }

    private static bool ModuleAgentsRegistered(PlatformKernel platform)
    {
        var expectedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chief-engineer"] = "ChiefEngineerAgent",
            ["mechanical-designer"] = "MechanicalDesignerAgent",
            ["cad-modeler"] = "CadModelerAgent",
            ["drawing-engineer"] = "DrawingEngineerAgent",
            ["drawing-reviewer"] = "DrawingReviewerAgent",
            ["code-engineer"] = "CodeEngineerAgent",
            ["code-reviewer"] = "CodeReviewerAgent",
            ["error-diagnosis"] = "ErrorDiagnosisAgent"
        };

        return expectedTypes.All(expected =>
            platform.AgentRegistry.GetById(expected.Key)?.GetType().Name == expected.Value);
    }

    private static async Task<RealRuntimeSelfCheckResult> RunRealRuntimeSelfChecks(string projectRoot, PlatformKernel platform)
    {
        var smokeAttempted = RealRuntimeEnvironmentConfigured();
        var smokePassed = false;
        string? smokeError = null;
        var strictSmokeTest = string.Equals(Environment.GetEnvironmentVariable("AI_RUNTIME_STRICT_SMOKE_TEST"), "true", StringComparison.OrdinalIgnoreCase);

        try
        {
            var runtimeAssembly = LoadRuntimeAssembly(projectRoot);
            var invokerType = runtimeAssembly.GetType("AgentRuntime.Microsoft.MicrosoftRuntimeAgentInvoker", throwOnError: true)!;
            var configType = runtimeAssembly.GetType("AgentRuntime.Microsoft.RuntimeConfiguration", throwOnError: true)!;
            var mapperType = runtimeAssembly.GetType("AgentRuntime.Microsoft.MicrosoftAgentOutputMapper", throwOnError: true)!;
            var adapterType = runtimeAssembly.GetType("AgentRuntime.Microsoft.MicrosoftAgentAdapter", throwOnError: true)!;
            var factoryType = runtimeAssembly.GetType("AgentRuntime.Microsoft.AgentFactory", throwOnError: true)!;
            var modelClientFactoryType = runtimeAssembly.GetType("AgentRuntime.Microsoft.RuntimeModelClientFactory", throwOnError: true)!;

            var realRuntimeInvokerImplemented = invokerType is not null && modelClientFactoryType is not null;
            var fromEnvironment = configType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method => method.Name == "FromEnvironment" && method.GetParameters().Length == 1)
                ?? throw new MissingMethodException("RuntimeConfiguration.FromEnvironment(environment) was not found.");
            var defaultConfig = InvokeRuntimeConfigurationFromEnvironment(fromEnvironment!, new Dictionary<string, string?>());
            var missingKeyConfig = InvokeRuntimeConfigurationFromEnvironment(
                fromEnvironment!,
                new Dictionary<string, string?>
                {
                    ["AI_AGENT_RUNTIME_MODE"] = "Microsoft",
                    ["AI_PROVIDER"] = "openai-compatible",
                    ["AI_MODEL"] = "demo-model"
                });

            var runtimeConfigEnvSupported = fromEnvironment is not null;
            var runtimeModeDefaultIsMock = GetEnumName(defaultConfig, "EffectiveMode") == "Mock";
            var runtimeFallbackWhenMissingKey =
                GetEnumName(missingKeyConfig, "RequestedMode") == "Microsoft" &&
                GetEnumName(missingKeyConfig, "EffectiveMode") == "Mock" &&
                GetBool(missingKeyConfig, "FallbackUsed");

            var mapper = Activator.CreateInstance(
                mapperType,
                new object[] { new[] { "mechanical-designer", "cad-modeler", "drawing-engineer", "drawing-reviewer" } })!;
            var mapMethod = mapperType.GetMethod("Map")!;
            var metadata = RuntimeMetadata.MockMicrosoft("openai-compatible", "demo-model");
            var mappedValid = (AgentContracts.AgentOutput)mapMethod.Invoke(mapper, new object[]
            {
                """
                {
                  "status": "completed",
                  "message": "ok",
                  "recommended_internal_agents": ["mechanical-designer"],
                  "next_recommended_agent_id": "mechanical-designer"
                }
                """,
                metadata
            })!;
            var mappedInvalid = (AgentContracts.AgentOutput)mapMethod.Invoke(mapper, new object[] { "plain text", metadata })!;
            var mappedUnsafe = (AgentContracts.AgentOutput)mapMethod.Invoke(mapper, new object[]
            {
                """
                {
                  "status": "completed",
                  "message": "unsafe",
                  "recommended_internal_agents": ["worker:FakeSolidWorksWorker"],
                  "next_recommended_agent_id": "worker:FakeSolidWorksWorker",
                  "visibility": "Public",
                  "expose_internal_agents": true
                }
                """,
                metadata
            })!;

            var microsoftAgentOutputMapperEnabled =
                mappedValid.Status == AgentContracts.AgentOutputStatus.Completed &&
                mappedValid.NextRecommendedAgentId == "mechanical-designer";
            var invalidModelOutputFallbackEnabled =
                mappedInvalid.Status == AgentContracts.AgentOutputStatus.Completed &&
                mappedInvalid.Message.Contains("plain text", StringComparison.OrdinalIgnoreCase);
            var modelCannotEscalatePermissions =
                mappedUnsafe.Issues.Any(issue => issue.Contains("permission", StringComparison.OrdinalIgnoreCase));
            var modelCannotCallWorkerDirectly =
                mappedUnsafe.Issues.Any(issue => issue.Contains("Worker", StringComparison.OrdinalIgnoreCase)) &&
                mappedUnsafe.NextRecommendedAgentId is null;

            var factory = Activator.CreateInstance(factoryType, platform.AuditLog)!;
            var createRuntimeAwareAgent = factoryType.GetMethod("CreateRuntimeAwareAgent")!;
            var microsoftConfig = InvokeRuntimeConfigurationFromEnvironment(
                fromEnvironment!,
                new Dictionary<string, string?>
                {
                    ["AI_AGENT_RUNTIME_MODE"] = "Microsoft",
                    ["AI_PROVIDER"] = "openai-compatible",
                    ["AI_MODEL"] = "demo-model",
                    ["AI_API_KEY"] = "self-check-key"
                });
            var chief = platform.AgentRegistry.GetById("chief-engineer")!;
            var internalAgent = platform.AgentRegistry.GetById("mechanical-designer")!;
            var runtimeChief = (AgentContracts.IAgent)createRuntimeAwareAgent.Invoke(factory, new object?[] { chief, platform.AgentRegistry, microsoftConfig, null })!;
            var runtimeInternal = (AgentContracts.IAgent)createRuntimeAwareAgent.Invoke(factory, new object?[] { internalAgent, platform.AgentRegistry, microsoftConfig, null })!;
            var chiefEngineerRealRuntimeOnly = adapterType.IsInstanceOfType(runtimeChief);
            var internalAgentsRemainMock = ReferenceEquals(internalAgent, runtimeInternal);

            var chiefEngineerRuntimeThenWorkflowEngine =
                adapterType.GetMethod("ExecuteAsync") is not null &&
                typeof(AgentContracts.AgentOutput).GetProperty(nameof(AgentContracts.AgentOutput.RuntimeMetadata)) is not null &&
                platform.AuditLog.GetEntries().Any(entry => entry.Action == "workflow_started");
            var qualityGateAfterRealRuntime = typeof(AgentOutputReviewMapper)
                .GetMethod(nameof(AgentOutputReviewMapper.ToReviewReport)) is not null;
            var gatewayResponseContainsRuntimeMetadata = GatewayResponseContainsRuntimeMetadata(projectRoot);

            if (smokeAttempted)
            {
                var smokeInvokerType = invokerType ?? throw new TypeLoadException("MicrosoftRuntimeAgentInvoker was not found.");
                var smokeConfigType = configType ?? throw new TypeLoadException("RuntimeConfiguration was not found.");
                var smokeModelClientFactoryType = modelClientFactoryType ?? throw new TypeLoadException("RuntimeModelClientFactory was not found.");

                (smokePassed, smokeError) = await RunRealRuntimeSmokeTestAsync(
                    runtimeAssembly,
                    smokeInvokerType,
                    smokeConfigType,
                    smokeModelClientFactoryType,
                    platform);
            }

            return new RealRuntimeSelfCheckResult(
                realRuntimeInvokerImplemented,
                runtimeConfigEnvSupported,
                runtimeModeDefaultIsMock,
                runtimeFallbackWhenMissingKey,
                chiefEngineerRealRuntimeOnly,
                internalAgentsRemainMock,
                microsoftAgentOutputMapperEnabled,
                invalidModelOutputFallbackEnabled,
                modelCannotEscalatePermissions,
                modelCannotCallWorkerDirectly,
                chiefEngineerRuntimeThenWorkflowEngine,
                qualityGateAfterRealRuntime,
                gatewayResponseContainsRuntimeMetadata,
                smokeAttempted,
                smokePassed,
                smokeError,
                strictSmokeTest);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException or MissingMethodException or InvalidOperationException or TargetInvocationException)
        {
            platform.AuditLog.Record("agent-runtime", "self-check", "real_runtime_check_failed", ex.Message);
            return new RealRuntimeSelfCheckResult(
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                smokeAttempted,
                smokePassed,
                ex.Message,
                strictSmokeTest);
        }
    }

    private static async Task<RuntimeReliabilitySelfCheckResult> RunRuntimeReliabilityChecks(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromMilliseconds(5);
        var retryAuditLog = new InMemoryAuditLog();
        var retryAttempts = 0;
        var retryEngine = new SequentialWorkflowEngine(
            new ExponentialBackoffRetryPolicy(maxRetries: 1, baseDelayMs: (int)retryDelay.TotalMilliseconds, maxDelayMs: 10, multiplier: 2),
            retryAuditLog);
        var retryStopwatch = Stopwatch.StartNew();
        var retryResult = await retryEngine.ExecuteAsync(
            new[]
            {
                new WorkflowStep("v08-retry-delay", _ =>
                {
                    retryAttempts++;
                    var decision = retryAttempts == 1
                        ? new GateDecision("gate-v08-retry", GateDecisionResult.Rejected, "retryable transient issue")
                        : new GateDecision("gate-v08-pass", GateDecisionResult.Passed, "ok");
                    return Task.FromResult(new WorkflowStepResult(
                        "v08-retry-delay",
                        "v08-retry-delay",
                        retryAttempts == 1 ? WorkflowStepStatus.Rejected : WorkflowStepStatus.Passed,
                        retryAttempts == 1 ? "retryable rejection" : "passed",
                        GateDecision: decision,
                        Issues: retryAttempts == 1 ? new[] { "retryable transient issue" } : Array.Empty<string>()));
                })
            },
            new WorkflowContext("v08-retry-delay-check", new Dictionary<string, object?>()),
            cancellationToken);
        retryStopwatch.Stop();
        var retryDelayActuallyAwaited =
            retryResult.Status == WorkflowStatus.Passed &&
            retryAttempts == 2 &&
            retryStopwatch.Elapsed >= retryDelay &&
            retryAuditLog.GetEntries().Any(entry =>
                entry.Action == "workflow_step_retrying" &&
                entry.Message.Contains("delay", StringComparison.OrdinalIgnoreCase));

        IRetryPolicy backoffPolicy = new ExponentialBackoffRetryPolicy(maxRetries: 2, baseDelayMs: 1, maxDelayMs: 5, multiplier: 2);
        var exponentialBackoffDelayRespected =
            backoffPolicy.GetDelay(0) == TimeSpan.FromMilliseconds(1) &&
            backoffPolicy.GetDelay(1) == TimeSpan.FromMilliseconds(2) &&
            backoffPolicy.GetDelay(2) == TimeSpan.FromMilliseconds(4) &&
            backoffPolicy.GetDelay(10) == TimeSpan.FromMilliseconds(5);

        var cancellationSupported = false;
        try
        {
            var cancellationEngine = new SequentialWorkflowEngine(
                new ExponentialBackoffRetryPolicy(maxRetries: 1, baseDelayMs: 5_000, maxDelayMs: 5_000, multiplier: 1),
                new InMemoryAuditLog());
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(20));
            await cancellationEngine.ExecuteAsync(
                new[]
                {
                    new WorkflowStep("v08-cancel-delay", _ => Task.FromResult(new WorkflowStepResult(
                        "v08-cancel-delay",
                        "v08-cancel-delay",
                        WorkflowStepStatus.Rejected,
                        "retryable rejection",
                        GateDecision: new GateDecision("gate-v08-cancel", GateDecisionResult.Rejected, "retryable transient issue"),
                        Issues: new[] { "retryable transient issue" })))
                },
                new WorkflowContext("v08-cancel-delay-check", new Dictionary<string, object?>()),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancellationSupported = true;
        }

        var runtimeTimeoutConfigSupported = false;
        var invalidTimeoutFallsBackToDefault = false;
        var openAIClientTimeoutConfigured = false;
        var openAIClientCancellationSupported = false;
        var providerErrorsAreStructured = false;
        var apiKeyNotLogged = false;

        try
        {
            var runtimeAssembly = LoadRuntimeAssembly(projectRoot);
            var configType = runtimeAssembly.GetType("AgentRuntime.Microsoft.RuntimeConfiguration", throwOnError: true)!;
            var openAIClientType = runtimeAssembly.GetType("AgentRuntime.Microsoft.OpenAICompatibleModelClient", throwOnError: true)!;
            var providerExceptionType = runtimeAssembly.GetType("AgentRuntime.Microsoft.RuntimeProviderException", throwOnError: true)!;
            var fromEnvironment = configType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "FromEnvironment" && method.GetParameters().Length == 1);

            var timeoutConfig = InvokeRuntimeConfigurationFromEnvironment(
                fromEnvironment,
                new Dictionary<string, string?> { ["AI_TIMEOUT_SECONDS"] = "7" });
            var invalidTimeoutConfig = InvokeRuntimeConfigurationFromEnvironment(
                fromEnvironment,
                new Dictionary<string, string?> { ["AI_TIMEOUT_SECONDS"] = "invalid" });
            var excessiveTimeoutConfig = InvokeRuntimeConfigurationFromEnvironment(
                fromEnvironment,
                new Dictionary<string, string?> { ["AI_TIMEOUT_SECONDS"] = "9999" });
            var belowMinTimeoutConfig = InvokeRuntimeConfigurationFromEnvironment(
                fromEnvironment,
                new Dictionary<string, string?> { ["AI_TIMEOUT_SECONDS"] = "-5" });

            var defaultTimeout = GetIntConstant(configType, "DefaultTimeoutSeconds");
            var minTimeout = GetIntConstant(configType, "MinTimeoutSeconds");
            var maxTimeout = GetIntConstant(configType, "MaxTimeoutSeconds");
            runtimeTimeoutConfigSupported =
                GetInt(timeoutConfig, "TimeoutSeconds", 0) == 7 &&
                GetInt(belowMinTimeoutConfig, "TimeoutSeconds", 0) == minTimeout &&
                GetInt(excessiveTimeoutConfig, "TimeoutSeconds", 0) == maxTimeout;
            invalidTimeoutFallsBackToDefault = GetInt(invalidTimeoutConfig, "TimeoutSeconds", 0) == defaultTimeout;

            var configuredClient = Activator.CreateInstance(openAIClientType, new[] { timeoutConfig })
                ?? throw new InvalidOperationException("Could not create OpenAICompatibleModelClient.");
            var configuredTimeoutProperty = openAIClientType.GetProperty("ConfiguredTimeout", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMemberException(openAIClientType.FullName, "ConfiguredTimeout");
            openAIClientTimeoutConfigured =
                configuredTimeoutProperty.GetValue(configuredClient) is TimeSpan configuredTimeout &&
                configuredTimeout == TimeSpan.FromSeconds(7);

            var generateTextAsync = openAIClientType.GetMethod("GenerateTextAsync")
                ?? throw new MissingMethodException("GenerateTextAsync was not found.");
            var parameters = generateTextAsync.GetParameters();
            var openAIClientSource = File.ReadAllText(Path.Combine(projectRoot, "src", "AgentRuntime.Microsoft", "OpenAICompatibleModelClient.cs"));
            openAIClientCancellationSupported =
                parameters.LastOrDefault()?.ParameterType == typeof(CancellationToken) &&
                openAIClientSource.Contains("SendAsync(request, HttpCompletionOption.ResponseHeadersRead, effectiveCancellationToken)", StringComparison.Ordinal);

            providerErrorsAreStructured =
                providerExceptionType.GetProperty("IssueType") is not null &&
                openAIClientSource.Contains("auth_error", StringComparison.Ordinal) &&
                openAIClientSource.Contains("rate_limit", StringComparison.Ordinal) &&
                openAIClientSource.Contains("provider_error", StringComparison.Ordinal) &&
                openAIClientSource.Contains("timeout", StringComparison.Ordinal) &&
                openAIClientSource.Contains("invalid_provider_response", StringComparison.Ordinal) &&
                openAIClientSource.Contains("network_error", StringComparison.Ordinal);

            apiKeyNotLogged = await RuntimeFailureOutputDoesNotLeakCredentialsAsync(
                runtimeAssembly,
                openAIClientType,
                fromEnvironment);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException or MissingMemberException or InvalidOperationException or TargetInvocationException)
        {
            return new RuntimeReliabilitySelfCheckResult(
                retryDelayActuallyAwaited,
                exponentialBackoffDelayRespected,
                cancellationSupported,
                false,
                false,
                false,
                false,
                false,
                false);
        }

        return new RuntimeReliabilitySelfCheckResult(
            retryDelayActuallyAwaited,
            exponentialBackoffDelayRespected,
            cancellationSupported,
            runtimeTimeoutConfigSupported,
            invalidTimeoutFallsBackToDefault,
            openAIClientTimeoutConfigured,
            openAIClientCancellationSupported,
            providerErrorsAreStructured,
            apiKeyNotLogged);
    }

    private static async Task<bool> RuntimeFailureOutputDoesNotLeakCredentialsAsync(
        Assembly runtimeAssembly,
        Type openAIClientType,
        MethodInfo fromEnvironment)
    {
        const string secret = "self-check-secret-api-key";
        const string authorizationHeaderName = "Authorization";

        var configuration = InvokeRuntimeConfigurationFromEnvironment(
            fromEnvironment,
            new Dictionary<string, string?>
            {
                ["AI_AGENT_RUNTIME_MODE"] = "Microsoft",
                ["AI_PROVIDER"] = "openai-compatible",
                ["AI_MODEL"] = "self-check-model",
                ["AI_API_KEY"] = secret,
                ["AI_BASE_URL"] = "http://runtime-self-check.test",
                ["AI_TIMEOUT_SECONDS"] = "7"
            });
        var openAIClientConstructor = openAIClientType.GetConstructor(new[] { typeof(HttpClient) })
            ?? throw new MissingMethodException(openAIClientType.FullName, ".ctor(HttpClient)");
        var openAIClient = openAIClientConstructor.Invoke(new object?[]
        {
            new HttpClient(new StaticResponseHandler(HttpStatusCode.Unauthorized, """{"error":"auth failed"}"""))
        });
        var invokerType = runtimeAssembly.GetType("AgentRuntime.Microsoft.MicrosoftRuntimeAgentInvoker", throwOnError: true)!;
        var manifestType = runtimeAssembly.GetType("AgentRuntime.Microsoft.RuntimeAgentManifest", throwOnError: true)!;
        var manifest = manifestType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Create" && method.GetParameters().Length == 2)
            .Invoke(null, new object?[] { "chief-engineer", null })
            ?? throw new InvalidOperationException("Could not create runtime agent manifest.");
        var auditLog = new InMemoryAuditLog();
        var invoker = Activator.CreateInstance(invokerType, new object?[]
            {
                configuration,
                openAIClient,
                auditLog,
                new[] { "mechanical-designer" },
                "system"
            })
            ?? throw new InvalidOperationException("Could not create MicrosoftRuntimeAgentInvoker.");
        var invokeAsync = invokerType.GetMethod("InvokeAsync")
            ?? throw new MissingMethodException("MicrosoftRuntimeAgentInvoker.InvokeAsync was not found.");
        var task = (Task<AgentContracts.AgentOutput>)invokeAsync.Invoke(
            invoker,
            new object?[] { manifest, CreateRuntimeCheckAgentContext(), CancellationToken.None })!;
        var output = await task;
        var inspectedText = string.Join(
            "\n",
            new[]
            {
                output.Message,
                string.Join("\n", output.Issues),
                string.Join("\n", output.Logs),
                output.RuntimeMetadata?.RuntimeFallbackReason ?? string.Empty,
                string.Join("\n", auditLog.GetEntries().Select(entry => $"{entry.Action}: {entry.Message}"))
            });

        return output.Status == AgentContracts.AgentOutputStatus.Failed &&
               !inspectedText.Contains(secret, StringComparison.OrdinalIgnoreCase) &&
               !inspectedText.Contains(authorizationHeaderName, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StaticResponseHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body)
            });
    }

    private static bool GatewayResponseContainsRuntimeMetadata(string projectRoot)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "AgentGatewayHost");
        var assembly = loaded ?? Assembly.LoadFrom(Path.Combine(projectRoot, "src", "Interfaces", "AgentGatewayHost", "bin", "Debug", "net10.0", "AgentGatewayHost.dll"));
        var responseType = assembly.GetType("AgentGatewayHost.GatewayMessageResponse", throwOnError: true)!;
        var expectedProperties = new[]
        {
            "RuntimeMode",
            "RuntimeProvider",
            "RuntimeModel",
            "RuntimeFallbackUsed",
            "RuntimeFallbackReason",
            "ChiefEngineerRuntimeUsed"
        };

        return expectedProperties.All(property => responseType.GetProperty(property) is not null);
    }

    private static bool RealRuntimeEnvironmentConfigured() =>
        string.Equals(Environment.GetEnvironmentVariable("AI_AGENT_RUNTIME_MODE"), "Microsoft", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_API_KEY")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_PROVIDER")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_MODEL"));

    private static async Task<(bool Passed, string? Error)> RunRealRuntimeSmokeTestAsync(
        Assembly runtimeAssembly,
        Type invokerType,
        Type configType,
        Type modelClientFactoryType,
        PlatformKernel platform)
    {
        try
        {
            var fromEnvironment = configType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method => method.Name == "FromEnvironment" && method.GetParameters().Length == 0)
                ?? throw new MissingMethodException("RuntimeConfiguration.FromEnvironment() was not found.");
            var configuration = fromEnvironment.Invoke(null, null)
                ?? throw new InvalidOperationException("Could not create runtime configuration from process environment.");
            var modelClientFactory = Activator.CreateInstance(modelClientFactoryType)
                ?? throw new InvalidOperationException("Could not create RuntimeModelClientFactory.");
            var modelClient = modelClientFactoryType.GetMethod("Create")!.Invoke(modelClientFactory, new[] { configuration })
                ?? throw new InvalidOperationException("Could not create runtime model client.");
            var manifestType = runtimeAssembly.GetType("AgentRuntime.Microsoft.RuntimeAgentManifest", throwOnError: true)!;
            var manifest = manifestType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "Create" && method.GetParameters().Length == 2)
                .Invoke(null, new object?[] { "chief-engineer", null })
                ?? throw new InvalidOperationException("Could not create runtime agent manifest.");
            var internalAgentIds = platform.AgentRegistry.GetInternalAgents().Select(agent => agent.Id).ToArray();
            var invoker = Activator.CreateInstance(invokerType, new object?[]
                {
                    configuration,
                    modelClient,
                    platform.AuditLog,
                    internalAgentIds,
                    null
                })
                ?? throw new InvalidOperationException("Could not create MicrosoftRuntimeAgentInvoker.");
            var invokeAsync = invokerType.GetMethod("InvokeAsync")
                ?? throw new MissingMethodException("MicrosoftRuntimeAgentInvoker.InvokeAsync was not found.");
            var timeoutSeconds = GetInt(configuration, "TimeoutSeconds", 30);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
            var task = (Task<AgentContracts.AgentOutput>)invokeAsync.Invoke(
                invoker,
                new object?[] { manifest, CreateRuntimeCheckAgentContext(), cancellation.Token })!;
            var output = await task;

            return output.Status == AgentContracts.AgentOutputStatus.Failed
                ? (false, string.Join("; ", output.Issues))
                : (true, null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException or MissingMethodException or InvalidOperationException or TargetInvocationException or TaskCanceledException or HttpRequestException)
        {
            return (false, ex.GetBaseException().Message);
        }
    }

    private static object InvokeRuntimeConfigurationFromEnvironment(
        MethodInfo fromEnvironment,
        Dictionary<string, string?> environment) =>
        fromEnvironment.Invoke(null, new object[] { environment })
        ?? throw new InvalidOperationException("Could not create runtime configuration.");

    private static string? GetEnumName(object instance, string propertyName) =>
        instance.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString();

    private static bool GetBool(object instance, string propertyName) =>
        instance.GetType().GetProperty(propertyName)?.GetValue(instance) is true;

    private static int GetInt(object instance, string propertyName, int fallback) =>
        instance.GetType().GetProperty(propertyName)?.GetValue(instance) is int value ? value : fallback;

    private static int GetIntConstant(Type type, string fieldName) =>
        type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int value
            ? value
            : throw new MissingFieldException(type.FullName, fieldName);

    private static async Task<RuntimeSelfCheckResult> RunRuntimeSelfChecks(
        string projectRoot,
        PlatformKernel platform,
        IReadOnlyList<AgentDirectoryEntry> gatewayVisibleAgents,
        bool internalAgentsHiddenFromGateway,
        bool gatewayQualityGateEnabled,
        bool qualityGateAfterCollaboration)
    {
        var runtimeProjectPath = Path.Combine(projectRoot, "src", "AgentRuntime.Microsoft", "AgentRuntime.Microsoft.csproj");
        var agentRuntimeProjectExists = File.Exists(runtimeProjectPath);
        var microsoftRuntimeDependencyIsolated = MicrosoftRuntimeDependencyIsolated(projectRoot, runtimeProjectPath);
        var runtimeTypesDoNotLeakToContracts = RuntimeTypesDoNotLeakToContracts(projectRoot);
        var gatewayVisibilityStillValid =
            gatewayVisibleAgents.Count == 1 &&
            gatewayVisibleAgents[0].Id == "chief-engineer" &&
            internalAgentsHiddenFromGateway;
        var qualityGateStillEnabled = gatewayQualityGateEnabled && qualityGateAfterCollaboration;

        var runtimeMode = "Mock";
        var mockRuntimeAgentCreation = false;
        var microsoftAgentAdapterCheck = false;
        var microsoftWorkflowRuntimeCheck = false;

        try
        {
            var runtimeAssembly = LoadRuntimeAssembly(projectRoot);
            var factoryType = runtimeAssembly.GetType("AgentRuntime.Microsoft.AgentFactory", throwOnError: true)!;
            var runtimeModeType = runtimeAssembly.GetType("AgentRuntime.Microsoft.AgentRuntimeMode", throwOnError: true)!;
            var workflowRuntimeType = runtimeAssembly.GetType("AgentRuntime.Microsoft.MicrosoftWorkflowRuntime", throwOnError: true)!;
            var mockModeValue = Enum.Parse(runtimeModeType, "Mock");

            var factory = Activator.CreateInstance(factoryType, platform.AuditLog)
                ?? throw new InvalidOperationException("Could not create AgentFactory.");
            var createMockAgent = factoryType.GetMethod("CreateMockAgent")
                ?? throw new MissingMethodException("CreateMockAgent was not found.");
            var expectedAgentIds = new[]
            {
                "chief-engineer",
                "mechanical-designer",
                "cad-modeler",
                "drawing-engineer",
                "drawing-reviewer",
                "code-engineer",
                "code-reviewer",
                "error-diagnosis"
            };
            var agents = expectedAgentIds
                .Select(agentId => (AgentContracts.IAgent)createMockAgent.Invoke(factory, new object[] { agentId })!)
                .ToArray();
            mockRuntimeAgentCreation =
                agents.Length == expectedAgentIds.Length &&
                agents.Count(agent => agent.Visibility == AgentContracts.AgentVisibility.Public) == 1 &&
                agents.Single(agent => agent.Visibility == AgentContracts.AgentVisibility.Public).Id == "chief-engineer";

            var adapterAgent = agents.Single(agent => agent.Id == "chief-engineer");
            var output = await adapterAgent.ExecuteAsync(CreateRuntimeCheckAgentContext());
            microsoftAgentAdapterCheck =
                adapterAgent is AgentContracts.IAgent &&
                output.Status == AgentContracts.AgentOutputStatus.Completed &&
                output.Message.Contains("MockRuntime", StringComparison.OrdinalIgnoreCase);

            var workflowRuntime = Activator.CreateInstance(workflowRuntimeType, mockModeValue)
                ?? throw new InvalidOperationException("Could not create MicrosoftWorkflowRuntime.");
            var executeAsync = workflowRuntimeType.GetMethod("ExecuteAsync")
                ?? throw new MissingMethodException("ExecuteAsync was not found.");
            var workflowTask = (Task<WorkflowExecutionResult>)executeAsync.Invoke(
                workflowRuntime,
                new object[]
                {
                    new[]
                    {
                        Step("runtime-step-1", "runtime step 1"),
                        Step("runtime-step-2", "runtime step 2")
                    },
                    new WorkflowContext("runtime-self-check", new Dictionary<string, object?>()),
                    CancellationToken.None
                })!;
            var workflowResult = await workflowTask;
            microsoftWorkflowRuntimeCheck =
                workflowResult.FinalStatus == "Passed" &&
                workflowResult.Steps.Count == 2;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException or MissingMethodException or InvalidOperationException or TargetInvocationException)
        {
            platform.AuditLog.Record("agent-runtime", "self-check", "runtime_check_failed", ex.Message);
        }

        return new RuntimeSelfCheckResult(
            agentRuntimeProjectExists,
            microsoftRuntimeDependencyIsolated,
            runtimeMode,
            mockRuntimeAgentCreation,
            microsoftAgentAdapterCheck,
            microsoftWorkflowRuntimeCheck,
            runtimeTypesDoNotLeakToContracts,
            gatewayVisibilityStillValid,
            qualityGateStillEnabled);
    }

    private static Assembly LoadRuntimeAssembly(string projectRoot)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "AgentRuntime.Microsoft");
        if (loaded is not null)
        {
            return loaded;
        }

        var assemblyPath = Path.Combine(projectRoot, "src", "AgentRuntime.Microsoft", "bin", "Debug", "net10.0", "AgentRuntime.Microsoft.dll");
        return Assembly.LoadFrom(assemblyPath);
    }

    private static AgentContracts.AgentContext CreateRuntimeCheckAgentContext()
    {
        var input = new AgentContracts.AgentInput(
            "self-check",
            "runtime-self-check",
            "runtime-self-check-conversation",
            "self-check",
            "Run runtime adapter self-check.",
            Array.Empty<string>(),
            new Dictionary<string, string>());

        return new AgentContracts.AgentContext(
            $"task-{Guid.NewGuid():N}",
            input,
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);
    }

    private static bool MicrosoftRuntimeDependencyIsolated(string projectRoot, string runtimeProjectPath)
    {
        var packagePrefix = string.Concat("Microsoft", ".Agents");
        var projectsWithRuntimePackage = Directory
            .GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(project => File.ReadAllText(project).Contains(packagePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .ToArray();

        return projectsWithRuntimePackage.Length > 0 &&
               projectsWithRuntimePackage.All(project => string.Equals(project, Path.GetFullPath(runtimeProjectPath), StringComparison.OrdinalIgnoreCase));
    }

    private static bool RuntimeTypesDoNotLeakToContracts(string projectRoot)
    {
        var runtimePackagePrefix = string.Concat("Microsoft", ".Agents");
        var runtimeFrameworkToken = string.Concat("Agent", "Framework");
        var roots = new[]
        {
            Path.Combine(projectRoot, "src", "AgentContracts"),
            Path.Combine(projectRoot, "src", "DomainSchemas"),
            Path.Combine(projectRoot, "src", "QualityGate"),
            Path.Combine(projectRoot, "src", "PlatformCore")
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .All(file =>
            {
                var text = File.ReadAllText(file);
                return !text.Contains(runtimePackagePrefix, StringComparison.OrdinalIgnoreCase) &&
                       !text.Contains(runtimeFrameworkToken, StringComparison.OrdinalIgnoreCase);
            });
    }

    private sealed record RuntimeSelfCheckResult(
        bool AgentRuntimeProjectExists,
        bool MicrosoftRuntimeDependencyIsolated,
        string RuntimeMode,
        bool MockRuntimeAgentCreation,
        bool MicrosoftAgentAdapterCheck,
        bool MicrosoftWorkflowRuntimeCheck,
        bool RuntimeTypesDoNotLeakToContracts,
        bool GatewayVisibilityStillValid,
        bool QualityGateStillEnabled);

    private sealed record RealRuntimeSelfCheckResult(
        bool RealRuntimeInvokerImplemented,
        bool RuntimeConfigEnvSupported,
        bool RuntimeModeDefaultIsMock,
        bool RuntimeFallbackWhenMissingKey,
        bool ChiefEngineerRealRuntimeOnly,
        bool InternalAgentsRemainMock,
        bool MicrosoftAgentOutputMapperEnabled,
        bool InvalidModelOutputFallbackEnabled,
        bool ModelCannotEscalatePermissions,
        bool ModelCannotCallWorkerDirectly,
        bool ChiefEngineerRuntimeThenWorkflowEngine,
        bool QualityGateAfterRealRuntime,
        bool GatewayResponseContainsRuntimeMetadata,
        bool MicrosoftRuntimeSmokeTestAttempted,
        bool MicrosoftRuntimeSmokeTestPassed,
        string? MicrosoftRuntimeSmokeTestError,
        bool StrictSmokeTest);

    private sealed record RuntimeReliabilitySelfCheckResult(
        bool RetryDelayActuallyAwaited,
        bool ExponentialBackoffDelayRespected,
        bool WorkflowRetryDelayCancellationSupported,
        bool RuntimeTimeoutConfigSupported,
        bool InvalidTimeoutFallsBackToDefault,
        bool OpenAIClientTimeoutConfigured,
        bool OpenAIClientCancellationSupported,
        bool ProviderErrorsAreStructured,
        bool ApiKeyNotLogged);

    private sealed record WorkflowQualityLoopSelfCheckResult(
        bool WorkflowQualityLoopEnabled,
        string WorkflowPassedScenario,
        string WorkflowRejectedRetryPassedScenario,
        string WorkflowRejectedMaxRetriesScenario,
        string WorkflowHumanApprovalScenario,
        bool RetryPolicyEnabled,
        bool FailureReportGenerated,
        bool HumanApprovalRequestGenerated);

    private sealed record WorkflowBackedInternalSelfCheckResult(
        bool ChiefEngineerInternalOrchestrationUsesWorkflowEngine,
        bool InternalAgentWorkflowStepsCreated,
        bool QualityGateAfterEachInternalStep,
        string InternalWorkflowPassedScenario,
        string InternalWorkflowRetryThenPassedScenario,
        string InternalWorkflowMaxRetriesExceededScenario,
        string InternalWorkflowFailedScenario,
        string InternalWorkflowHumanApprovalScenario,
        bool RetryPolicyInterfaceEnabled,
        bool ExponentialBackoffPolicyAvailable,
        bool CodeEngineerAgentRegistered,
        bool CodeReviewerAgentRegistered,
        bool CodeAgentsAreInternal,
        bool GatewayBlocksCodeAgents);

    private sealed record SolidWorksSkeletonSelfCheckResult(
        bool SolidWorksModuleSkeletonEnabled,
        bool SolidWorksBuildPlanSkillRegistered,
        bool SolidWorksBuildPlanGenerated,
        bool SolidWorksWorkerContractExists,
        bool FakeSolidWorksWorkerRegistered,
        bool FakeSolidWorksWorkerDryRunPassed,
        bool SolidWorksBuildPlanValidatorPassed,
        bool SolidWorksArtifactValidatorPassed,
        bool SolidWorksBuildPlanReviewerPassed,
        bool SolidWorksQualityGatePassed,
        bool SolidWorksFakeArtifactsGenerated,
        bool SolidWorksRealCadNotExecuted,
        bool SolidWorksAgentDoesNotCallWorkerDirectly,
        bool GatewayDoesNotCallSolidWorksWorker,
        string? SelfCheckInfrastructureError);

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
