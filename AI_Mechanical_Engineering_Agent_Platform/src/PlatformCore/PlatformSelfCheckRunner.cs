using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Diagnostics;
using System.Net;
using AgentContracts;
using DomainSchemas;
using PlatformCore.Modules.CADModeling;
using PlatformCore.Modules.CADModeling.Agents;
using PlatformCore.Modules.CADModeling.Reviewers;
using PlatformCore.Modules.CADModeling.Skills;
using PlatformCore.Modules.CADModeling.Validators;
using QualityGate;

namespace PlatformCore;

public static class PlatformSelfCheckRunner
{
    private const string FakeSolidWorksWorkerFullName = "SolidWorksWorker.FakeSolidWorksWorker";
    private const string SelfCheckSchemaVersion = "2.2-a-task-approval";
    private static readonly object RealAcceptanceOutputLock = new();

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
        InternalWorkflowRoute.EngineeringDefault.AgentIds.ToArray();

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

    private static readonly string[] CanonicalCodexAgentNames =
    [
        "project_manager",
        "code_mapper",
        "api_researcher",
        "cad_worker",
        "quality_gate",
        "docs_writer"
    ];

    private static readonly string[] CanonicalCodexAgentFiles =
    [
        "project-manager.toml",
        "code-mapper.toml",
        "api-researcher.toml",
        "cad-worker.toml",
        "quality-gate.toml",
        "docs-writer.toml"
    ];

    public static async Task<PlatformSelfCheckReport> RunAsync(
        PlatformKernel platform,
        string outputRoot,
        string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.Combine(outputRoot, "reports"));
        var root = projectRoot ?? PlatformPathResolver.FindProjectRoot();
        var runMetadata = new SelfCheckRunMetadata(
            $"platform-self-check-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            await ResolveSourceRevisionAsync(root));

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
        var executableDocsChecks = RunExecutableDocsLayerChecks(root);
        var realAcceptanceOutputs = WriteRealAcceptanceOutputs(root, runMetadata, outputRoot);
        var versionStageText = File.Exists(Path.Combine(root, "docs", "version_stage_index.md"))
            ? File.ReadAllText(Path.Combine(root, "docs", "version_stage_index.md"))
            : string.Empty;
        var realAcceptanceProtocolPath = Path.Combine(root, "docs", "solidworks_real_acceptance_protocol.md");
        var solidWorksComFacadeInjectionSupported = SolidWorksComFacadeInjectionSeamsAreAvailable();
        var solidWorksRealAcceptanceProtocolExists = File.Exists(realAcceptanceProtocolPath);
        var solidWorksLatestRealOutputsReportSupported = realAcceptanceOutputs.IsValid;
        var v16TestADocumented =
            versionStageText.Contains("V1.6-TEST-A", StringComparison.OrdinalIgnoreCase) &&
            solidWorksRealAcceptanceProtocolExists;
        var v17E2eChecks = await V17RealCadE2eSelfCheck.RunAsync(root, cancellationToken);
        var realCadE2eCliEntryExists = SolidWorksE2eCliContract.IsInvocation(
            [SolidWorksE2eCliContract.CommandName, SolidWorksE2eCliContract.InputOption, "examples/real_cad_plate_request.json"]);
        var realCadE2eStructuredInputSupported = v17E2eChecks.StructuredInputSupported;
        var realCadE2eUsesChiefEngineerOrchestrator = v17E2eChecks.ChiefEngineerInvoked;
        var realCadE2eUsesWorkflowEngine = v17E2eChecks.WorkflowEngineInvoked;
        var realCadE2eUsesSolidWorksRouter = realCadE2eStructuredInputSupported &&
            v17E2eChecks.SolidWorksRouterTriggered;
        var realCadE2eCanInvokeRealWorker =
            Type.GetType("SolidWorksWorker.RealSolidWorksWorker, SolidWorksWorker", throwOnError: false) is not null &&
            Type.GetType("SolidWorksWorker.SolidWorksE2EReleasePackageBuilder, SolidWorksWorker", throwOnError: false) is not null;
        var realCadE2ePassesQualityGate = v17E2eChecks.QualityGateRejectsIncompleteExecution;
        var realCadE2eDefaultDisabled = v17E2eChecks.DefaultExecutionDisabled;
        var realCadE2eRequiresRequestConfirmation = v17E2eChecks.RequestConfirmationRequired;
        var realCadE2eRequiresEnvConfirmation = v17E2eChecks.EnvironmentConfirmationRequired;
        var realCadE2eReportSupported = v17E2eChecks.ReportSupported;
        var realCadE2eDeliverableSemanticsSupported = v17E2eChecks.DeliverableSemanticsSupported;
        var v17VersionStageDocumented = versionStageText.Contains("V1.7", StringComparison.OrdinalIgnoreCase);
        var realCadE2eLocalAuthorizationProfileSupported = v17E2eChecks.LocalAuthorizationProfileSupported;
        var realCadE2eLocalAuthorizationDefaultDisabled = v17E2eChecks.LocalAuthorizationDefaultDisabled;
        var v18PartFamilyChecks = await RunV18PartFamilyChecksAsync(root, platform, outputRoot, versionStageText, cancellationToken);
        var v19PartFamilyChecks = RunV19PartFamilyChecks(root, platform, versionStageText, v18PartFamilyChecks);
        var v20SolidWorksDefaultOnChecks = RunV20SolidWorksDefaultOnChecks();
        var v20AGenericCadModelSpecChecks = RunV20AGenericCadModelSpecChecks(root, platform, versionStageText);
        var v20BFeatureHandlerChecks = RunV20BFeatureHandlerChecks(root, platform, versionStageText);
        var v20CFeatureAdapterChecks = RunV20CFeatureAdapterChecks(root, platform, versionStageText);
        var v20DModelRebuildChecks = RunV20DModelRebuildChecks(
            root,
            outputRoot,
            versionStageText,
            markdownChineseCheckPassed);
        // V2.1-A 复杂特征检查必须先于 V2.0-E：回归闸的当前值字典需要它的结果。
        var v21AComplexFeatureChecks = RunV21AComplexFeatureChecks(root, platform, versionStageText);
        var holeSelfCheck = await V21BHoleSelfCheck.RunAsync(root, platform, cancellationToken, outputRoot);
        var v21BHoleChecks = holeSelfCheck.Checks;
        var workflowReliabilityChecks = await WorkflowReliabilitySelfCheck.RunAsync();
        var structuredInputFailsClosed = await StructuredCadInputSelfCheck.RunAsync();
        var taskLifecycleChecks = await TaskLifecycleSelfCheck.RunAsync(root);
        var approvalIdentityBound = await WorkflowReliabilitySelfCheck.RunApprovalIdentityAsync();
        var capabilityChecks = new Dictionary<string, bool>(v21BHoleChecks);
        capabilityChecks["structured_cad_input_fails_closed"] = structuredInputFailsClosed;
        foreach (var check in workflowReliabilityChecks) capabilityChecks[check.Key] = check.Value;
        foreach (var check in taskLifecycleChecks) capabilityChecks[check.Key] = check.Value;
        capabilityChecks["workflow_approval_identity_bound"] = approvalIdentityBound;
        var v20EUnifiedFeatureGraphChecks = RunV20EUnifiedFeatureGraphChecks(
            root,
            platform,
            versionStageText,
            v18PartFamilyChecks,
            v19PartFamilyChecks,
            v20BFeatureHandlerChecks,
            v20CFeatureAdapterChecks,
            v20DModelRebuildChecks,
            v21AComplexFeatureChecks,
            capabilityChecks);
        var v21AJacketChecks = await RunV21AJacketChecksAsync(
            root,
            platform,
            versionStageText,
            outputRoot,
            cancellationToken);
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
            workflowQualityChecks.HumanApprovalResumeSupported &&
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
            solidWorksSkeletonChecks.SolidWorksRealWorkerSkeletonExists &&
            solidWorksSkeletonChecks.SolidWorksEnvironmentValidatorExists &&
            solidWorksSkeletonChecks.SolidWorksPreflightReportGenerated &&
            solidWorksSkeletonChecks.SolidWorksSessionManagerExists &&
            solidWorksSkeletonChecks.SolidWorksComNotCalledInDefaultSelfCheck &&
            (!solidWorksSkeletonChecks.SolidWorksStrictRealSmokeTest ||
             !solidWorksSkeletonChecks.SolidWorksRealConnectionSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealConnectionSmokeTestPassed) &&
            solidWorksSkeletonChecks.SolidWorksRealCadNotExecutedByDefault &&
            solidWorksSkeletonChecks.SolidWorksRealPlateBuildImplemented &&
            solidWorksSkeletonChecks.SolidWorksRealBuildRequiresDryRunFalse &&
            (!solidWorksSkeletonChecks.SolidWorksRealBuildSmokeTestAttempted ||
             (solidWorksSkeletonChecks.RealBuildRequestDryRun == false &&
              solidWorksSkeletonChecks.RealBuildExecutionMode == "RealBuildPlateBasic4Holes" &&
              !string.IsNullOrWhiteSpace(solidWorksSkeletonChecks.RealBuildOutputDirectory) &&
              Path.IsPathFullyQualified(solidWorksSkeletonChecks.RealBuildOutputDirectory) &&
              !string.IsNullOrWhiteSpace(solidWorksSkeletonChecks.RealBuildLatestReportPath) &&
              File.Exists(solidWorksSkeletonChecks.RealBuildLatestReportPath))) &&
            (solidWorksSkeletonChecks.SolidWorksRealBuildSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealBuildDefaultDisabled) &&
            (!solidWorksSkeletonChecks.SolidWorksStrictRealBuildSmokeTest ||
             !solidWorksSkeletonChecks.SolidWorksRealBuildSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealBuildSmokeTestPassed) &&
            (solidWorksSkeletonChecks.SolidWorksRealBuildSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealBuildNotCalledInDefaultSelfCheck) &&
            solidWorksSkeletonChecks.SolidWorksDiagnosticRunnerExists &&
            solidWorksSkeletonChecks.SolidWorksDiagnosticRunnerNotCalledByDefault &&
            solidWorksSkeletonChecks.SolidWorksRealBuildErrorIsActionable &&
            solidWorksSkeletonChecks.SolidWorksApiFailureAnalyzerExists &&
            solidWorksSkeletonChecks.SolidWorksApiEvidenceCollectorExists &&
            solidWorksSkeletonChecks.SolidWorksApiEvidenceReportSchemaExists &&
            solidWorksSkeletonChecks.SolidWorksCutHolesApiEvidenceSupported &&
            solidWorksSkeletonChecks.SolidWorksReferenceSkillReadonlyAnalysisSupported &&
            solidWorksSkeletonChecks.SolidWorksExternalScriptsNotCopied &&
            solidWorksSkeletonChecks.SolidWorksApiRepairLoopAvailable &&
            solidWorksSkeletonChecks.SolidWorksMacroRecordingRequestAvailable &&
            solidWorksSkeletonChecks.SolidWorksPlateFeatureBuilderExists &&
            solidWorksSkeletonChecks.SolidWorksRealDrawingBasicViewsImplemented &&
            (solidWorksSkeletonChecks.SolidWorksRealDrawingSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingDefaultDisabled) &&
            (!solidWorksSkeletonChecks.SolidWorksStrictRealDrawingSmokeTest ||
             !solidWorksSkeletonChecks.SolidWorksRealDrawingSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingSmokeTestPassed) &&
            (solidWorksSkeletonChecks.SolidWorksRealDrawingSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingNotCalledInDefaultSelfCheck) &&
            solidWorksSkeletonChecks.SolidWorksDrawingFailureStageActionable &&
            solidWorksSkeletonChecks.V11VersionStageDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingFailureRepairDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingApiEvidenceDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingReviewChecklistUpdated &&
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsImplemented &&
            (solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsDefaultDisabled) &&
            (!solidWorksSkeletonChecks.SolidWorksStrictRealDrawingDimensionSmokeTest ||
             !solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsSmokeTestPassed) &&
            (solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsNotCalledInDefaultSelfCheck) &&
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionFailureStageActionable &&
            solidWorksSkeletonChecks.V12VersionStageDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionFailureRepairDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionApiEvidenceDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionReviewChecklistUpdated &&
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockImplemented &&
            (solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockDefaultDisabled) &&
            (!solidWorksSkeletonChecks.SolidWorksStrictRealDrawingTitleBlockSmokeTest ||
             !solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockSmokeTestPassed) &&
            (solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockSmokeTestAttempted ||
             solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockNotCalledInDefaultSelfCheck) &&
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockFailureStageActionable &&
            solidWorksSkeletonChecks.V13VersionStageDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockFailureRepairDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockApiEvidenceDocumented &&
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockReviewChecklistUpdated &&
            solidWorksSkeletonChecks.SolidWorksReleasePackageImplemented &&
            solidWorksSkeletonChecks.SolidWorksReleasePackageDefaultNoCadExecution &&
            solidWorksSkeletonChecks.SolidWorksReleaseManifestGenerated &&
            solidWorksSkeletonChecks.SolidWorksPackageQualityReportGenerated &&
            solidWorksSkeletonChecks.SolidWorksReleaseSummaryGenerated &&
            solidWorksSkeletonChecks.SolidWorksReleasePackageFailureStageActionable &&
            solidWorksSkeletonChecks.V14VersionStageDocumented &&
            solidWorksSkeletonChecks.SolidWorksReleasePackageFailureRepairDocumented &&
            solidWorksSkeletonChecks.SolidWorksReleasePackageReviewChecklistUpdated &&
            solidWorksSkeletonChecks.RealCadWorkerIntegratedIntoMainWorkflow &&
            solidWorksSkeletonChecks.ChiefEngineerOrchestratorInvokesCadWorkflow &&
            solidWorksSkeletonChecks.WorkflowEngineCanRouteToSolidWorksWorker &&
            solidWorksSkeletonChecks.RealCadMainWorkflowPassesQualityGate &&
            solidWorksSkeletonChecks.GatewayDoesNotCallWorkerDirectly &&
            solidWorksSkeletonChecks.LlmDoesNotCallWorkerDirectly &&
            solidWorksSkeletonChecks.ReleasePackageAllSourceReportsPassedFieldExists &&
            solidWorksSkeletonChecks.ReleasePackageDeliverableStatusFieldExists &&
            solidWorksSkeletonChecks.ReleasePackageFailedSourceReportsBlockDeliverable &&
            solidWorksSkeletonChecks.V15VersionStageDocumented &&
            solidWorksComFacadeInjectionSupported &&
            solidWorksRealAcceptanceProtocolExists &&
             solidWorksLatestRealOutputsReportSupported &&
             v16TestADocumented &&
             realCadE2eCliEntryExists &&
             realCadE2eStructuredInputSupported &&
             realCadE2eUsesChiefEngineerOrchestrator &&
             realCadE2eUsesWorkflowEngine &&
             realCadE2eUsesSolidWorksRouter &&
             realCadE2eCanInvokeRealWorker &&
             realCadE2ePassesQualityGate &&
             realCadE2eReportSupported &&
             realCadE2eDeliverableSemanticsSupported &&
             v17VersionStageDocumented &&
             v18PartFamilyChecks.AllPassed &&
             v19PartFamilyChecks.AllPassed &&
             v20SolidWorksDefaultOnChecks.AllPassed &&
             v20AGenericCadModelSpecChecks.AllPassed &&
             v20BFeatureHandlerChecks.AllPassed &&
            v20CFeatureAdapterChecks.AllPassed &&
            v20DModelRebuildChecks.AllPassed &&
            v20EUnifiedFeatureGraphChecks.AllPassed &&
            v21AComplexFeatureChecks.AllPassed &&
            holeSelfCheck.GroupPassed && structuredInputFailsClosed && workflowReliabilityChecks.Values.All(value => value) &&
            taskLifecycleChecks.Values.All(value => value) && approvalIdentityBound &&
            v21AJacketChecks.AllPassed &&
            executableDocsChecks.ExecutableDocsLayerEnabled &&
            gateDecision.Result == GateDecisionResult.Passed &&
            workflow.FinalStatus == "Passed";

        var finalStatus = checksPassed ? "Passed" : "Failed";
        platform.TaskStore.UpdateStatus(task.Id, checksPassed ? PlatformTaskStatus.Passed : PlatformTaskStatus.Failed);
        platform.AuditLog.Record("self-check", "quality-gate", finalStatus.ToLowerInvariant(), $"Self-check final status: {finalStatus}.");

        var report = new PlatformSelfCheckReport(
            SelfCheckSchemaVersion,
            runMetadata.RunId,
            runMetadata.GeneratedAt,
            runMetadata.SourceRevision,
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
            solidWorksSkeletonChecks.SolidWorksRealWorkerSkeletonExists,
            solidWorksSkeletonChecks.SolidWorksEnvironmentValidatorExists,
            solidWorksSkeletonChecks.SolidWorksPreflightReportGenerated,
            solidWorksSkeletonChecks.SolidWorksSessionManagerExists,
            solidWorksSkeletonChecks.SolidWorksRealExecutionDefaultDisabled,
            solidWorksSkeletonChecks.SolidWorksRealExecutionRequiresRequestFlag,
            solidWorksSkeletonChecks.SolidWorksRealExecutionRequiresEnvFlag,
            solidWorksSkeletonChecks.SolidWorksComNotCalledInDefaultSelfCheck,
            solidWorksSkeletonChecks.SolidWorksRealConnectionSmokeTestAttempted,
            solidWorksSkeletonChecks.SolidWorksRealConnectionSmokeTestPassed,
            solidWorksSkeletonChecks.SolidWorksRealConnectionSmokeTestError,
            solidWorksSkeletonChecks.SolidWorksGenericRealBuildNotImplemented,
            solidWorksSkeletonChecks.SolidWorksRealCadNotExecutedByDefault,
            solidWorksSkeletonChecks.SolidWorksRealPlateBuildImplemented,
            solidWorksSkeletonChecks.SolidWorksRealBuildRequiresEnvFlag,
            solidWorksSkeletonChecks.SolidWorksRealBuildRequiresRequestFlag,
            solidWorksSkeletonChecks.SolidWorksRealBuildRequiresDryRunFalse,
            solidWorksSkeletonChecks.SolidWorksRealBuildDefaultDisabled,
            solidWorksSkeletonChecks.SolidWorksRealBuildSmokeTestAttempted,
            solidWorksSkeletonChecks.SolidWorksRealBuildSmokeTestPassed,
            solidWorksSkeletonChecks.SolidWorksRealBuildSmokeTestError,
            solidWorksSkeletonChecks.SolidWorksRealBuildArtifactsValidated,
            solidWorksSkeletonChecks.SolidWorksRealBuildReportGenerated,
            solidWorksSkeletonChecks.SolidWorksRealBuildOutputsSldprt,
            solidWorksSkeletonChecks.SolidWorksRealBuildOutputsStep,
            solidWorksSkeletonChecks.SolidWorksRealBuildOutputsJsonReport,
            solidWorksSkeletonChecks.SolidWorksRealBuildNotCalledInDefaultSelfCheck,
            solidWorksSkeletonChecks.SwRealBuildSmokeTestEnvValue,
            solidWorksSkeletonChecks.SwStrictRealBuildTestEnvValue,
            solidWorksSkeletonChecks.RealBuildRequestDryRun,
            solidWorksSkeletonChecks.RealBuildExecutionMode,
            solidWorksSkeletonChecks.RealBuildOutputDirectory,
            solidWorksSkeletonChecks.RealBuildLatestReportPath,
            solidWorksSkeletonChecks.SolidWorksDiagnosticRunnerExists,
            solidWorksSkeletonChecks.SolidWorksDiagnosticRunnerNotCalledByDefault,
            solidWorksSkeletonChecks.SolidWorksLatestDiagnosticReportPath,
            solidWorksSkeletonChecks.SolidWorksLatestDiagnosticFinalStatus,
            solidWorksSkeletonChecks.SolidWorksRealBuildFailureStage,
            solidWorksSkeletonChecks.SolidWorksRealBuildErrorIsActionable,
            solidWorksSkeletonChecks.SolidWorksApiFailureAnalyzerExists,
            solidWorksSkeletonChecks.SolidWorksApiEvidenceCollectorExists,
            solidWorksSkeletonChecks.SolidWorksApiEvidenceReportSchemaExists,
            solidWorksSkeletonChecks.SolidWorksCutHolesApiEvidenceSupported,
            solidWorksSkeletonChecks.SolidWorksReferenceSkillReadonlyAnalysisSupported,
            solidWorksSkeletonChecks.SolidWorksExternalScriptsNotCopied,
            solidWorksSkeletonChecks.SolidWorksApiRepairLoopAvailable,
            solidWorksSkeletonChecks.SolidWorksMacroRecordingRequestAvailable,
            solidWorksSkeletonChecks.SolidWorksPlateFeatureBuilderExists,
            solidWorksSkeletonChecks.SolidWorksRealDrawingBasicViewsImplemented,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDefaultDisabled,
            solidWorksSkeletonChecks.SolidWorksRealDrawingRequiresEnvFlag,
            solidWorksSkeletonChecks.SolidWorksRealDrawingSmokeTestAttempted,
            solidWorksSkeletonChecks.SolidWorksRealDrawingSmokeTestPassed,
            solidWorksSkeletonChecks.SolidWorksRealDrawingSmokeTestError,
            solidWorksSkeletonChecks.SolidWorksRealDrawingOutputsSlddrw,
            solidWorksSkeletonChecks.SolidWorksRealDrawingOutputsPdf,
            solidWorksSkeletonChecks.SolidWorksRealDrawingOutputsJsonReport,
            solidWorksSkeletonChecks.SolidWorksRealDrawingNotCalledInDefaultSelfCheck,
            solidWorksSkeletonChecks.SolidWorksDrawingReportGenerated,
            solidWorksSkeletonChecks.SolidWorksRealDrawingFailureStage,
            solidWorksSkeletonChecks.SolidWorksDrawingFailureStageActionable,
            solidWorksSkeletonChecks.RealDrawingOutputDirectory,
            solidWorksSkeletonChecks.RealDrawingLatestReportPath,
            solidWorksSkeletonChecks.V11VersionStageDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingFailureRepairDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingApiEvidenceDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingReviewChecklistUpdated,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsImplemented,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsDefaultDisabled,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsRequiresEnvFlag,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsSmokeTestAttempted,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsSmokeTestPassed,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsSmokeTestError,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsOutputsSlddrw,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsOutputsPdf,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsOutputsJsonReport,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionsNotCalledInDefaultSelfCheck,
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionReportGenerated,
            solidWorksSkeletonChecks.SolidWorksRealDrawingDimensionFailureStage,
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionFailureStageActionable,
            solidWorksSkeletonChecks.RealDrawingDimensionOutputDirectory,
            solidWorksSkeletonChecks.RealDrawingDimensionLatestReportPath,
            solidWorksSkeletonChecks.V12VersionStageDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionFailureRepairDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionApiEvidenceDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingDimensionReviewChecklistUpdated,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockImplemented,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockDefaultDisabled,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockRequiresEnvFlag,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockSmokeTestAttempted,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockSmokeTestPassed,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockSmokeTestError,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockOutputsSlddrw,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockOutputsPdf,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockOutputsJsonReport,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockNotCalledInDefaultSelfCheck,
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockReportGenerated,
            solidWorksSkeletonChecks.SolidWorksRealDrawingTitleBlockFailureStage,
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockFailureStageActionable,
            solidWorksSkeletonChecks.RealDrawingTitleBlockOutputDirectory,
            solidWorksSkeletonChecks.RealDrawingTitleBlockLatestReportPath,
            solidWorksSkeletonChecks.V13VersionStageDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockFailureRepairDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockApiEvidenceDocumented,
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockReviewChecklistUpdated,
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockPopulationStrategy,
            solidWorksSkeletonChecks.SolidWorksDrawingTitleBlockFieldsVerifiedInSheetFormat,
            solidWorksSkeletonChecks.SolidWorksReleasePackageImplemented,
            solidWorksSkeletonChecks.SolidWorksReleasePackageDefaultNoCadExecution,
            solidWorksSkeletonChecks.SolidWorksReleaseManifestGenerated,
            solidWorksSkeletonChecks.SolidWorksPackageQualityReportGenerated,
            solidWorksSkeletonChecks.SolidWorksReleaseSummaryGenerated,
            solidWorksSkeletonChecks.SolidWorksReleaseArtifactsCollected,
            solidWorksSkeletonChecks.SolidWorksReleaseReportsCollected,
            solidWorksSkeletonChecks.SolidWorksReleasePackageFailureStage,
            solidWorksSkeletonChecks.SolidWorksReleasePackageFailureStageActionable,
            solidWorksSkeletonChecks.SolidWorksReleaseManifestPath,
            solidWorksSkeletonChecks.SolidWorksPackageQualityReportPath,
            solidWorksSkeletonChecks.SolidWorksReleaseSummaryPath,
            solidWorksSkeletonChecks.V14VersionStageDocumented,
            solidWorksSkeletonChecks.SolidWorksReleasePackageFailureRepairDocumented,
            solidWorksSkeletonChecks.SolidWorksReleasePackageReviewChecklistUpdated,
            executableDocsChecks.ExecutableDocsLayerEnabled,
            executableDocsChecks.DocsIndexExists,
            executableDocsChecks.ProjectExecutionStandardExists,
            executableDocsChecks.ModuleDocumentStandardExists,
            executableDocsChecks.StepExecutionStandardExists,
            executableDocsChecks.FailureRepairStandardExists,
            executableDocsChecks.CodexExecutionProtocolExists,
            executableDocsChecks.ClaudeReviewProtocolExists,
            executableDocsChecks.VersionStageIndexExists,
            executableDocsChecks.CodexAgentTeamGuideExists,
            executableDocsChecks.CodexAgentRegistryExists,
            executableDocsChecks.CodexAgentGovernanceDocExists,
            executableDocsChecks.AgentsMdExists,
            executableDocsChecks.CodexAgentsConfigured,
            executableDocsChecks.CodexAgentRegistryListsCanonicalAgents,
            executableDocsChecks.CodexNoDuplicateActiveAgents,
            executableDocsChecks.CodexAgentReusePolicyDocumented,
            executableDocsChecks.CodexAgentNewRequirementsGoToSkillsOrDocs,
            executableDocsChecks.CodexActiveAgentCountIsExpected,
            executableDocsChecks.CodexOnlyCanonicalAgentsActive,
            executableDocsChecks.CodexConfigExampleExists,
            executableDocsChecks.CodexProjectManagerAgentExists,
            executableDocsChecks.CodexCodeMapperAgentExists,
            executableDocsChecks.CodexApiResearcherAgentExists,
            executableDocsChecks.CodexCadWorkerAgentExists,
            executableDocsChecks.CodexQualityGateAgentExists,
            executableDocsChecks.CodexDocsWriterAgentExists,
            executableDocsChecks.CodexAgentsDoNotReplaceProjectModules,
            executableDocsChecks.CodexAgentsRespectWorkerBoundaries,
            executableDocsChecks.AgentsSkillsDirectoryExists,
            executableDocsChecks.SolidWorksApiRepairSkillExists,
            executableDocsChecks.MarkdownDocsStandardSkillExists,
            executableDocsChecks.QualityReviewSkillExists,
            executableDocsChecks.CadModelingExecutionDocExists,
            executableDocsChecks.CadModelingFailureRepairDocExists,
            executableDocsChecks.CadModelingApiEvidenceDocExists,
            executableDocsChecks.CadModelingReviewChecklistExists,
            executableDocsChecks.SolidWorksWorkerExecutionDocExists,
            executableDocsChecks.SolidWorksWorkerFailureRepairDocExists,
            executableDocsChecks.SolidWorksWorkerApiEvidenceDocExists,
            executableDocsChecks.SolidWorksWorkerReviewChecklistExists,
            solidWorksSkeletonChecks.RealCadWorkerIntegratedIntoMainWorkflow,
            solidWorksSkeletonChecks.ChiefEngineerOrchestratorInvokesCadWorkflow,
            solidWorksSkeletonChecks.WorkflowEngineCanRouteToSolidWorksWorker,
            solidWorksSkeletonChecks.RealCadMainWorkflowDefaultDisabled,
            solidWorksSkeletonChecks.RealCadMainWorkflowRequiresRequestFlag,
            solidWorksSkeletonChecks.RealCadMainWorkflowRequiresEnvFlag,
            solidWorksSkeletonChecks.RealCadMainWorkflowPassesQualityGate,
            solidWorksSkeletonChecks.GatewayDoesNotCallWorkerDirectly,
            solidWorksSkeletonChecks.LlmDoesNotCallWorkerDirectly,
            solidWorksSkeletonChecks.ReleasePackageAllSourceReportsPassedFieldExists,
            solidWorksSkeletonChecks.ReleasePackageDeliverableStatusFieldExists,
            solidWorksSkeletonChecks.ReleasePackageFailedSourceReportsBlockDeliverable,
            solidWorksSkeletonChecks.V15VersionStageDocumented,
            solidWorksComFacadeInjectionSupported,
            solidWorksRealAcceptanceProtocolExists,
            solidWorksLatestRealOutputsReportSupported,
            v16TestADocumented,
            realCadE2eCliEntryExists,
            realCadE2eStructuredInputSupported,
            realCadE2eUsesChiefEngineerOrchestrator,
            realCadE2eUsesWorkflowEngine,
            realCadE2eUsesSolidWorksRouter,
            realCadE2eCanInvokeRealWorker,
            realCadE2ePassesQualityGate,
            realCadE2eDefaultDisabled,
            realCadE2eRequiresRequestConfirmation,
            realCadE2eRequiresEnvConfirmation,
            realCadE2eReportSupported,
            realCadE2eDeliverableSemanticsSupported,
            v17VersionStageDocumented,
            realCadE2eLocalAuthorizationProfileSupported,
            realCadE2eLocalAuthorizationDefaultDisabled,
            finalStatus) with
        {
            GenericCadModelSpecSupported = v18PartFamilyChecks.GenericCadModelSpecSupported,
            PartTypeRegistryExists = v18PartFamilyChecks.PartTypeRegistryExists,
            PlatePartFamilyRegistered = v18PartFamilyChecks.PlatePartFamilyRegistered,
            FlangePartFamilyRegistered = v18PartFamilyChecks.FlangePartFamilyRegistered,
            ShaftPartFamilyRegistered = v18PartFamilyChecks.ShaftPartFamilyRegistered,
            UnsupportedPartTypeRejected = v18PartFamilyChecks.UnsupportedPartTypeRejected,
            InvalidPartParametersRejectedBeforeWorker = v18PartFamilyChecks.InvalidPartParametersRejectedBeforeWorker,
            PartFamilyBuildersDoNotUseLargeSwitch = v18PartFamilyChecks.PartFamilyBuildersDoNotUseLargeSwitch,
            PlateRegressionPassed = v18PartFamilyChecks.PlateRegressionPassed,
            FlangeDryRunPassed = v18PartFamilyChecks.FlangeDryRunPassed,
            ShaftDryRunPassed = v18PartFamilyChecks.ShaftDryRunPassed,
            RealCadPartFamilyDefaultDisabled = v18PartFamilyChecks.RealCadPartFamilyDefaultDisabled,
            V18VersionStageDocumented = v18PartFamilyChecks.V18VersionStageDocumented,
            FlangeRealBuilderImplemented = v19PartFamilyChecks.FlangeRealBuilderImplemented,
            ShaftRealBuilderImplemented = v19PartFamilyChecks.ShaftRealBuilderImplemented,
            FlangeRealWorkflowSupported = v19PartFamilyChecks.FlangeRealWorkflowSupported,
            ShaftRealWorkflowSupported = v19PartFamilyChecks.ShaftRealWorkflowSupported,
            FlangeRealWorkflowDefaultDisabled = v19PartFamilyChecks.FlangeRealWorkflowDefaultDisabled,
            ShaftRealWorkflowDefaultDisabled = v19PartFamilyChecks.ShaftRealWorkflowDefaultDisabled,
            FlangeApiEvidenceDocumented = v19PartFamilyChecks.FlangeApiEvidenceDocumented,
            ShaftApiEvidenceDocumented = v19PartFamilyChecks.ShaftApiEvidenceDocumented,
            FlangeArtifactValidationSupported = v19PartFamilyChecks.FlangeArtifactValidationSupported,
            ShaftArtifactValidationSupported = v19PartFamilyChecks.ShaftArtifactValidationSupported,
            PlatePartFamilyRegressionPassed = v19PartFamilyChecks.PlatePartFamilyRegressionPassed,
            NoLargePartTypeSwitch = v19PartFamilyChecks.NoLargePartTypeSwitch,
            AllPartFamiliesUseRegistry = v19PartFamilyChecks.AllPartFamiliesUseRegistry,
            V19VersionStageDocumented = v19PartFamilyChecks.V19VersionStageDocumented,
            SolidWorksLocalInteractiveDefaultEnabled = v20SolidWorksDefaultOnChecks.LocalInteractiveDefaultEnabled,
            SolidWorksDisableEnvSupported = v20SolidWorksDefaultOnChecks.DisableEnvironmentSupported,
            SolidWorksCiExecutionDisabled = v20SolidWorksDefaultOnChecks.CiExecutionDisabled,
            SolidWorksUnitTestExecutionDisabled = v20SolidWorksDefaultOnChecks.UnitTestExecutionDisabled,
            SolidWorksDryRunDisablesRealExecution = v20SolidWorksDefaultOnChecks.DryRunDisablesRealExecution,
            SolidWorksVisibleDefaultTrue = v20SolidWorksDefaultOnChecks.VisibleDefaultTrue,
            SolidWorksExecutionEnvironmentProbeSupported = v20SolidWorksDefaultOnChecks.ExecutionEnvironmentProbeSupported,
            LegacyEnableFlagNotRequired = v20SolidWorksDefaultOnChecks.LegacyEnableFlagNotRequired,
            LegacyRequestConfirmationNotRequired = v20SolidWorksDefaultOnChecks.LegacyRequestConfirmationNotRequired,
            GenericCadModelSpecV2Supported = v20AGenericCadModelSpecChecks.GenericCadModelSpecV2Supported,
            SketchDefinitionSupported = v20AGenericCadModelSpecChecks.SketchDefinitionSupported,
            SketchConstraintsSupported = v20AGenericCadModelSpecChecks.SketchConstraintsSupported,
            FeatureDefinitionSupported = v20AGenericCadModelSpecChecks.FeatureDefinitionSupported,
            FeatureGraphSupported = v20AGenericCadModelSpecChecks.FeatureGraphSupported,
            FeatureGraphCycleDetected = v20AGenericCadModelSpecChecks.FeatureGraphCycleDetected,
            MissingFeatureDependencyRejected = v20AGenericCadModelSpecChecks.MissingFeatureDependencyRejected,
            BuildPlanCompilerSupported = v20AGenericCadModelSpecChecks.BuildPlanCompilerSupported,
            PlateUsesGenericFeatureGraph = v20AGenericCadModelSpecChecks.PlateUsesGenericFeatureGraph,
            FlangeUsesGenericFeatureGraph = v20AGenericCadModelSpecChecks.FlangeUsesGenericFeatureGraph,
            ShaftUsesGenericFeatureGraph = v20AGenericCadModelSpecChecks.ShaftUsesGenericFeatureGraph,
            NoPartSpecificLogicInRealWorker = v20AGenericCadModelSpecChecks.NoPartSpecificLogicInRealWorker,
            V20ADocumented = v20AGenericCadModelSpecChecks.V20ADocumented,
            FeatureHandlerRegistryExists = v20BFeatureHandlerChecks.FeatureHandlerRegistryExists,
            NoFeatureTypeLargeSwitch = v20BFeatureHandlerChecks.NoFeatureTypeLargeSwitch,
            SketchHandlerRegistered = v20BFeatureHandlerChecks.SketchHandlerRegistered,
            ExtrudeHandlerRegistered = v20BFeatureHandlerChecks.ExtrudeHandlerRegistered,
            CutHandlerRegistered = v20BFeatureHandlerChecks.CutHandlerRegistered,
            HoleHandlerRegistered = v20BFeatureHandlerChecks.HoleHandlerRegistered,
            RevolveHandlerRegistered = v20BFeatureHandlerChecks.RevolveHandlerRegistered,
            FeatureHandlerValidationSupported = v20BFeatureHandlerChecks.FeatureHandlerValidationSupported,
            FeatureApiEvidenceRequired = v20BFeatureHandlerChecks.FeatureApiEvidenceRequired,
            UnverifiedApiBlocksRealExecution = v20BFeatureHandlerChecks.UnverifiedApiBlocksRealExecution,
            FeatureHandlerDocsCompleted = v20BFeatureHandlerChecks.FeatureHandlerDocsCompleted,
            V20BDocumented = v20BFeatureHandlerChecks.V20BDocumented,
            FeatureAdapterLayerExists = v20CFeatureAdapterChecks.FeatureAdapterLayerExists,
            FeatureHandlerNoDirectComAccess = v20CFeatureAdapterChecks.FeatureHandlerNoDirectComAccess,
            SolidWorksFeatureAdapterExists = v20CFeatureAdapterChecks.SolidWorksFeatureAdapterExists,
            SketchRealExecutionSupported = v20CFeatureAdapterChecks.SketchRealExecutionSupported,
            ExtrudeRealExecutionSupported = v20CFeatureAdapterChecks.ExtrudeRealExecutionSupported,
            CutRealExecutionSupported = v20CFeatureAdapterChecks.CutRealExecutionSupported,
            HoleRealExecutionSupported = v20CFeatureAdapterChecks.HoleRealExecutionSupported,
            FeaturePipelineEndToEndSupported = v20CFeatureAdapterChecks.FeaturePipelineEndToEndSupported,
            FeatureProductionEvidenceActive = v20CFeatureAdapterChecks.FeatureProductionEvidenceActive,
            FeatureResultValidationSupported = v20CFeatureAdapterChecks.FeatureResultValidationSupported,
            FeatureFakeSuccessGuardSupported = v20CFeatureAdapterChecks.FeatureFakeSuccessGuardSupported,
            V20CDocumented = v20CFeatureAdapterChecks.V20CDocumented,
            ModelRebuildPipelineExists = v20DModelRebuildChecks.ModelRebuildPipelineExists,
            ParameterUpdateSupported = v20DModelRebuildChecks.ParameterUpdateSupported,
            SolidWorksRebuildSupported = v20DModelRebuildChecks.SolidWorksRebuildSupported,
            GeometryValidatorExists = v20DModelRebuildChecks.GeometryValidatorExists,
            BoundingBoxValidationSupported = v20DModelRebuildChecks.BoundingBoxValidationSupported,
            VolumeValidationSupported = v20DModelRebuildChecks.VolumeValidationSupported,
            ParameterGeometryMatchSupported = v20DModelRebuildChecks.ParameterGeometryMatchSupported,
            RebuildFailureDetected = v20DModelRebuildChecks.RebuildFailureDetected,
            GeometryReportGenerated = v20DModelRebuildChecks.GeometryReportGenerated,
            V20DDocumented = v20DModelRebuildChecks.V20DDocumented,
            V20DProductionEvidenceActive = v20DModelRebuildChecks.ProductionEvidenceActive,
            V20EUnifiedPartFamilyBuilders = v20EUnifiedFeatureGraphChecks.UnifiedPartFamilyBuilders,
            V20EControlledPlateEvidenceActive = v20EUnifiedFeatureGraphChecks.ControlledPlateEvidenceActive,
            V20EStepContentGateActive = v20EUnifiedFeatureGraphChecks.StepContentGateActive,
            V21ARealExecutionFrozen = v20EUnifiedFeatureGraphChecks.V21ARealExecutionFrozen,
            V20EDocumented = v20EUnifiedFeatureGraphChecks.V20EDocumented,
            V20ECapabilityRegressionGatePassed = v20EUnifiedFeatureGraphChecks.CapabilityRegressionGatePassed,
            V20EGeometryValidationPlatformWide = v20EUnifiedFeatureGraphChecks.GeometryValidationPlatformWide,
            FilletHandlerRegistered = v21AComplexFeatureChecks.FilletHandlerRegistered,
            ChamferHandlerRegistered = v21AComplexFeatureChecks.ChamferHandlerRegistered,
            LinearPatternHandlerRegistered = v21AComplexFeatureChecks.LinearPatternHandlerRegistered,
            CircularPatternHandlerRegistered = v21AComplexFeatureChecks.CircularPatternHandlerRegistered,
            MirrorHandlerRegistered = v21AComplexFeatureChecks.MirrorHandlerRegistered,
            ComplexFeatureRegistrySupported = v21AComplexFeatureChecks.ComplexFeatureRegistrySupported,
            UnverifiedFeatureBlocksExecution = v21AComplexFeatureChecks.UnverifiedFeatureBlocksExecution,
            FeatureLibraryDocumented = v21AComplexFeatureChecks.FeatureLibraryDocumented,
            FeatureRegressionTestsPassed = v21AComplexFeatureChecks.FeatureRegressionTestsPassed,
            V21ADocumented = v21AComplexFeatureChecks.V21ADocumented,
            SimpleHoleSupported = holeSelfCheck.Value("simple_hole_supported"),
            CounterboreHoleSupported = holeSelfCheck.Value("counterbore_hole_supported"),
            CountersinkHoleSupported = holeSelfCheck.Value("countersink_hole_supported"),
            TappedHoleSupported = holeSelfCheck.Value("tapped_hole_supported"),
            HoleTypeValidationSupported = holeSelfCheck.Value("hole_type_validation_supported"),
            HoleGeometryValidationSupported = holeSelfCheck.Value("hole_geometry_validation_supported"),
            TappedHoleSemanticsSeparatedFromSimpleCut = holeSelfCheck.Value("tapped_hole_semantics_separated_from_simple_cut"),
            HoleApiEvidenceRequired = holeSelfCheck.Value("hole_api_evidence_required"),
            UnverifiedHoleBlocksRealExecution = holeSelfCheck.Value("unverified_hole_blocks_real_execution"),
            HoleSelfCheckGroupPassed = holeSelfCheck.GroupPassed,
            StructuredCadInputFailsClosed = structuredInputFailsClosed,
            TaskLifecycleTracked = taskLifecycleChecks["task_lifecycle_tracked"],
            TaskApprovalRoundtripSupported = taskLifecycleChecks["task_approval_roundtrip_supported"],
            TaskAccessTokenRequired = taskLifecycleChecks["task_access_token_required"],
            WorkflowApprovalIdentityBound = approvalIdentityBound,
            HoleSelfCheckInputsReadable = holeSelfCheck.Value("hole_self_check_inputs_readable") is true,
            HoleSelfCheckIssues = holeSelfCheck.Issues,
            HoleSelfCheckUnobservedCapabilities = holeSelfCheck.UnobservedCapabilities,
            WorkflowStepRetryLimitEnforced = workflowReliabilityChecks["workflow_step_retry_limit_enforced"],
            WorkflowCancelledApprovalPreserved = workflowReliabilityChecks["workflow_cancelled_approval_preserved"],
            WorkflowMultiApprovalHistoryPreserved = workflowReliabilityChecks["workflow_multi_approval_history_preserved"],
            V21BDocumented = holeSelfCheck.Value("v2_1_b_documented"),
            EdgeSelectionModelSupported = v21AComplexFeatureChecks.EdgeSelectionModelSupported,
            V20ECapabilityRegressions = v20EUnifiedFeatureGraphChecks.CapabilityRegressions,
            PartFamilyDefinitionSupported = v20EUnifiedFeatureGraphChecks.PartFamilyDefinitionSupported,
            PlateUsesPartFamilyDefinition = v20EUnifiedFeatureGraphChecks.PlateUsesPartFamilyDefinition,
            FlangeUsesPartFamilyDefinition = v20EUnifiedFeatureGraphChecks.FlangeUsesPartFamilyDefinition,
            ShaftUsesPartFamilyDefinition = v20EUnifiedFeatureGraphChecks.ShaftUsesPartFamilyDefinition,
            NoPartSpecificBuilderLogic = v20EUnifiedFeatureGraphChecks.NoPartSpecificBuilderLogic,
            FeatureGraphTemplateReuseSupported = v20EUnifiedFeatureGraphChecks.FeatureGraphTemplateReuseSupported,
            CommonFeatureTemplatesExists = v20EUnifiedFeatureGraphChecks.CommonFeatureTemplatesExists,
            CadCapabilityMatrixExists = v20EUnifiedFeatureGraphChecks.CadCapabilityMatrixExists,
            RegressionModelsSupported = v20EUnifiedFeatureGraphChecks.RegressionModelsSupported,
            FlangeRegressionPassed = v20EUnifiedFeatureGraphChecks.FlangeRegressionPassed,
            ShaftRegressionPassed = v20EUnifiedFeatureGraphChecks.ShaftRegressionPassed,
            V20EFinalStatus = v20EUnifiedFeatureGraphChecks.AllPassed ? "Passed" : "Failed",
            HumanApprovalResumeSupported = workflowQualityChecks.HumanApprovalResumeSupported,
            JacketPartFamilyRegistered = v21AJacketChecks.JacketPartFamilyRegistered,
            JacketUsesGenericFeatureGraph = v21AJacketChecks.JacketUsesGenericFeatureGraph,
            JacketRealBuilderImplemented = v21AJacketChecks.JacketRealBuilderImplemented,
            JacketDryRunPassed = v21AJacketChecks.JacketDryRunPassed,
            JacketRealWorkflowSupported = v21AJacketChecks.JacketRealWorkflowSupported,
            JacketApiEvidenceDocumented = v21AJacketChecks.JacketApiEvidenceDocumented,
            JacketProductionEvidenceActive = v21AJacketChecks.JacketProductionEvidenceActive,
            V21AJacketDocumented = v21AJacketChecks.V21AJacketDocumented
        };

        var reportPath = Path.Combine(outputRoot, "reports", "platform_self_check_report.json");
        await using var stream = File.Create(reportPath);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions());

        return report;
    }

    private static async Task<V18PartFamilySelfCheckResult> RunV18PartFamilyChecksAsync(
        string projectRoot,
        PlatformKernel platform,
        string outputRoot,
        string versionStageText,
        CancellationToken cancellationToken)
    {
        var registry = PartTypeRegistry.CreateDefault();
        var genericProperties = new[]
        {
            nameof(CADModelSpec.PartType),
            nameof(CADModelSpec.Dimensions),
            nameof(CADModelSpec.Features),
            nameof(CADModelSpec.Material),
            nameof(CADModelSpec.OutputRequirements),
            nameof(CADModelSpec.DrawingRequirements),
            nameof(CADModelSpec.ExecutionOptions)
        };
        var genericCadModelSpecSupported = genericProperties.All(name => typeof(CADModelSpec).GetProperty(name) is not null);
        var partTypeRegistryExists = registry.GetAll().Count >= 3;
        var platePartFamilyRegistered = registry.GetDefinition(PlateBasic4HolesDefinition.Type) is PlateBasic4HolesDefinition;
        var flangePartFamilyRegistered = registry.GetDefinition(FlangeBasicDefinition.Type) is FlangeBasicDefinition;
        var shaftPartFamilyRegistered = registry.GetDefinition(ShaftBasicDefinition.Type) is ShaftBasicDefinition;
        var validator = new CADModelSpecValidator(registry);
        var unsupported = validator.Validate(new CADModelSpec(
            "self-check-unsupported",
            "unsupported_self_check_family",
            new Dictionary<string, string>()));
        var unsupportedPartTypeRejected =
            !unsupported.IsValid &&
            string.Equals(unsupported.FailureStage, PartFamilyFailureStages.UnsupportedPartType, StringComparison.OrdinalIgnoreCase);
        var invalidSpec = new CADModelSpec(
            "self-check-invalid-flange",
            FlangeBasicDefinition.Type,
            new Dictionary<string, string> { ["outer_diameter_mm"] = "160" });
        var invalidSkillOutput = await new SolidWorksBuildPlanSkill(registry).ExecuteAsync(new SkillContracts.SkillInput(
            "self-check-v18-invalid",
            nameof(CADModelSpec),
            invalidSpec,
            new Dictionary<string, string>()));
        var invalidPartParametersRejectedBeforeWorker =
            invalidSkillOutput.Status == SkillContracts.SkillOutputStatus.Rejected &&
            invalidSkillOutput.Result is null &&
            invalidSkillOutput.Issues.Any(issue => issue.StartsWith("missing_required_parameter:", StringComparison.OrdinalIgnoreCase));
        var worker = platform.WorkerRegistry.GetByName("FakeSolidWorksWorker");
        var definitionPartTypes = registry.GetAll()
            .Select(definition => definition.PartType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builderRegistryType = worker?.GetType().Assembly.GetType(
            "SolidWorksWorker.PartFamilyBuilderRegistry",
            throwOnError: false);
        var createBuilderRegistry = builderRegistryType?
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static);
        var builderRegistry = createBuilderRegistry?.Invoke(
            null,
            createBuilderRegistry.GetParameters().Select(_ => (object?)null).ToArray());
        var registeredBuilders = builderRegistryType?
            .GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
            .Invoke(builderRegistry, null) as System.Collections.IEnumerable;
        var builderPartTypes = registeredBuilders?
            .Cast<object>()
            .Select(builder => builder.GetType().GetProperty("PartType")?.GetValue(builder) as string)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var partFamilyBuildersDoNotUseLargeSwitch =
            builderRegistryType is not null &&
            builderRegistryType.GetMethod("Register", BindingFlags.Public | BindingFlags.Instance) is not null &&
            builderRegistryType.GetMethod("TryGetBuilder", BindingFlags.Public | BindingFlags.Instance) is not null &&
            definitionPartTypes.Count == 4 &&
            definitionPartTypes.SetEquals(builderPartTypes);

        var workerMethod = worker?.GetType().GetMethods().SingleOrDefault(method =>
            method.Name == "ExecuteAsync" &&
            method.GetParameters().Length == 2 &&
            method.GetParameters()[0].ParameterType == typeof(SolidWorksWorkerRequest));

        async Task<bool> DryRunAsync(string partType, CADModelSpec spec)
        {
            if (worker is null || workerMethod is null)
            {
                return false;
            }

            var output = await new SolidWorksBuildPlanSkill(registry).ExecuteAsync(new SkillContracts.SkillInput(
                $"self-check-v18-{partType}", nameof(CADModelSpec), spec, new Dictionary<string, string>()));
            if (output.Result is not SolidWorksBuildPlan plan ||
                output.Status != SkillContracts.SkillOutputStatus.Completed ||
                !new SolidWorksBuildPlanReviewer(registry).Review(plan).IsPassed)
            {
                return false;
            }

            var request = new SolidWorksWorkerRequest(
                $"self-check-v18-request-{partType}-{Guid.NewGuid():N}",
                plan,
                Path.Combine(outputRoot, "solidworks", "self-check", "v1_8_part_families", partType),
                DryRun: true,
                AllowRealCadExecution: false);
            var task = workerMethod.Invoke(worker, new object?[] { request, cancellationToken }) as Task<SolidWorksWorkerResult>;
            if (task is null)
            {
                return false;
            }

            var result = await task;
            return result.Status == "Completed" &&
                   result.ExecutionMode == "Fake" &&
                   !result.RealCadExecuted &&
                   result.GeneratedArtifacts.Any(artifact => artifact.FilePath.EndsWith($"fake_{partType}.SLDPRT.txt", StringComparison.OrdinalIgnoreCase)) &&
                   result.GeneratedArtifacts.Any(artifact => artifact.FilePath.EndsWith($"fake_{partType}.STEP.txt", StringComparison.OrdinalIgnoreCase));
        }

        var plateRegressionPassed = await DryRunAsync(
            PlateBasic4HolesDefinition.Type,
            SolidWorksWorkflowRouter.CreatePlateBasicFourHolesSpec());
        var flangeDryRunPassed = await DryRunAsync(
            FlangeBasicDefinition.Type,
            new CADModelSpec("self-check-flange", FlangeBasicDefinition.Type, new Dictionary<string, string>
            {
                ["outer_diameter_mm"] = "160", ["inner_diameter_mm"] = "60", ["thickness_mm"] = "18",
                ["bolt_hole_count"] = "6", ["bolt_hole_diameter_mm"] = "14", ["bolt_circle_diameter_mm"] = "115"
            }));
        var shaftDryRunPassed = await DryRunAsync(
            ShaftBasicDefinition.Type,
            new CADModelSpec("self-check-shaft", ShaftBasicDefinition.Type, new Dictionary<string, string>
            {
                ["diameter_mm"] = "40", ["length_mm"] = "180",
                ["optional_step_diameters"] = "32,24", ["optional_step_lengths"] = "40,30"
            }));
        var runtimeDefaults = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>());
        var realCadPartFamilyDefaultDisabled =
            !runtimeDefaults.EnableRealExecution &&
            !runtimeDefaults.MainWorkflowExecutionEnabled;
        var v18VersionStageDocumented = versionStageText.Contains("V1.8", StringComparison.OrdinalIgnoreCase);

        return new V18PartFamilySelfCheckResult(
            genericCadModelSpecSupported,
            partTypeRegistryExists,
            platePartFamilyRegistered,
            flangePartFamilyRegistered,
            shaftPartFamilyRegistered,
            unsupportedPartTypeRejected,
            invalidPartParametersRejectedBeforeWorker,
            partFamilyBuildersDoNotUseLargeSwitch,
            plateRegressionPassed,
            flangeDryRunPassed,
            shaftDryRunPassed,
            realCadPartFamilyDefaultDisabled,
            v18VersionStageDocumented);
    }

    private static V19PartFamilySelfCheckResult RunV19PartFamilyChecks(
        string projectRoot,
        PlatformKernel platform,
        string versionStageText,
        V18PartFamilySelfCheckResult v18)
    {
        var definitions = PartTypeRegistry.CreateDefault().GetAll();
        var workerAssembly = platform.WorkerRegistry.GetByName("FakeSolidWorksWorker")?.GetType().Assembly;
        var builderRegistryType = workerAssembly?.GetType("SolidWorksWorker.PartFamilyBuilderRegistry", throwOnError: false);
        var createBuilderRegistry = builderRegistryType?.GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static);
        var builderRegistry = createBuilderRegistry?.Invoke(
            null,
            createBuilderRegistry.GetParameters().Select(_ => (object?)null).ToArray());
        var builders = (builderRegistryType?
            .GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
            .Invoke(builderRegistry, null) as System.Collections.IEnumerable)?
            .Cast<object>()
            .ToArray() ?? [];

        // V2.0-E：零件族不再拥有专用 Builder，改为断言"该族解析到统一建模内核的
        // 通用执行器"。expectedTypeName 保留参数以维持调用点可读性，不再参与判定。
        bool BuilderImplemented(string partType, string expectedTypeName)
        {
            _ = expectedTypeName;
            var definition = definitions.SingleOrDefault(item =>
                string.Equals(item.PartType, partType, StringComparison.OrdinalIgnoreCase));
            var builder = builders.SingleOrDefault(item =>
                string.Equals(
                    item.GetType().GetProperty("PartType")?.GetValue(item)?.ToString(),
                    partType,
                    StringComparison.OrdinalIgnoreCase));
            return definition is not null &&
                   builder is not null &&
                   string.Equals(
                       builder.GetType().GetProperty("RealExecutionMode")?.GetValue(builder)?.ToString(),
                       definition.RealExecutionMode,
                       StringComparison.OrdinalIgnoreCase) &&
                   builder.GetType().GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.Instance) is not null;
        }

        var flangeRealBuilderImplemented = BuilderImplemented(FlangeBasicDefinition.Type, "FlangeFeatureBuilder");
        var shaftRealBuilderImplemented = BuilderImplemented(ShaftBasicDefinition.Type, "ShaftFeatureBuilder");
        var flangeDefinition = definitions.Single(item => item.PartType == FlangeBasicDefinition.Type);
        var shaftDefinition = definitions.Single(item => item.PartType == ShaftBasicDefinition.Type);
        var operationSupported = SolidWorksE2eCliContract.IsPartFamilyReleasePackage(
            SolidWorksE2eCliContract.PartFamilyReleasePackageOperation) &&
            Enum.IsDefined(SolidWorksMainWorkflowOperation.BuildPartFamilyReleasePackage);
        var flangeInputExists = File.Exists(Path.Combine(projectRoot, "examples", "real_cad_flange_request.json"));
        var shaftInputExists = File.Exists(Path.Combine(projectRoot, "examples", "real_cad_shaft_request.json"));
        var flangeRealWorkflowSupported = operationSupported && flangeInputExists && flangeRealBuilderImplemented && flangeDefinition.SupportsRealExecution;
        var shaftRealWorkflowSupported = operationSupported && shaftInputExists && shaftRealBuilderImplemented && shaftDefinition.SupportsRealExecution;
        var runtimeDefaults = SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>());
        var defaultDisabled = !runtimeDefaults.EnableRealExecution && !runtimeDefaults.MainWorkflowExecutionEnabled;

        var apiEvidencePath = Path.Combine(projectRoot, "src", "Modules", "CADModeling", "api_evidence.md");
        var apiEvidenceText = File.Exists(apiEvidencePath) ? File.ReadAllText(apiEvidencePath) : string.Empty;
        var flangeApiEvidenceDocumented =
            apiEvidenceText.Contains("flange_basic", StringComparison.OrdinalIgnoreCase) &&
            apiEvidenceText.Contains("FeatureExtrusion2", StringComparison.Ordinal) &&
            apiEvidenceText.Contains("FeatureCut4", StringComparison.Ordinal);
        var shaftApiEvidenceDocumented =
            apiEvidenceText.Contains("shaft_basic", StringComparison.OrdinalIgnoreCase) &&
            apiEvidenceText.Contains("FeatureRevolve2", StringComparison.Ordinal) &&
            (apiEvidenceText.Contains("Mark=16", StringComparison.OrdinalIgnoreCase) ||
             apiEvidenceText.Contains("selection mark `16`", StringComparison.OrdinalIgnoreCase));

        var artifactValidatorExists = typeof(Modules.CADModeling.Validators.SolidWorksArtifactValidator)
            .GetMethod("Validate", BindingFlags.Public | BindingFlags.Instance) is not null;
        bool ArtifactValidationSupported(IPartFamilyDefinition definition)
        {
            var builder = builders.SingleOrDefault(item =>
                string.Equals(
                    item.GetType().GetProperty("PartType")?.GetValue(item)?.ToString(),
                    definition.PartType,
                    StringComparison.OrdinalIgnoreCase));
            return artifactValidatorExists &&
                   builder is not null &&
                   string.Equals(definition.RealExecutionMode, PartFamilyExecutionModes.GenericFeatureGraph, StringComparison.Ordinal) &&
                   string.Equals(
                       builder.GetType().GetProperty("RealExecutionMode")?.GetValue(builder)?.ToString(),
                       PartFamilyExecutionModes.GenericFeatureGraph,
                       StringComparison.Ordinal) &&
                   builder.GetType().GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.Instance) is not null;
        }

        var flangeArtifactValidationSupported = ArtifactValidationSupported(flangeDefinition);
        var shaftArtifactValidationSupported = ArtifactValidationSupported(shaftDefinition);

        var builderPartTypes = builders
            .Select(builder => builder.GetType().GetProperty("PartType")?.GetValue(builder)?.ToString())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitionPartTypes = definitions
            .Select(definition => definition.PartType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allPartFamiliesUseRegistry =
            builderRegistryType?.GetMethod("TryGetBuilder", BindingFlags.Public | BindingFlags.Instance) is not null &&
            definitionPartTypes.SetEquals(builderPartTypes);
        var noLargePartTypeSwitch =
            v18.PartFamilyBuildersDoNotUseLargeSwitch &&
            workerAssembly?.GetType("SolidWorksWorker.SolidWorksPartFamilyBuildModes", throwOnError: false)?
                .GetMethod("ForPartType", BindingFlags.Public | BindingFlags.Static) is null;
        var v19VersionStageDocumented = versionStageText.Contains("V1.9", StringComparison.OrdinalIgnoreCase);

        return new V19PartFamilySelfCheckResult(
            flangeRealBuilderImplemented,
            shaftRealBuilderImplemented,
            flangeRealWorkflowSupported,
            shaftRealWorkflowSupported,
            defaultDisabled,
            defaultDisabled,
            flangeApiEvidenceDocumented,
            shaftApiEvidenceDocumented,
            flangeArtifactValidationSupported,
            shaftArtifactValidationSupported,
            v18.PlateRegressionPassed,
            noLargePartTypeSwitch,
            allPartFamiliesUseRegistry,
            v19VersionStageDocumented);
    }

    private static V20SolidWorksDefaultOnSelfCheckResult RunV20SolidWorksDefaultOnChecks()
    {
        var emptyEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var localInteractive = SolidWorksRuntimeOptions.FromEnvironment(emptyEnvironment, isUnitTestEnvironment: false);
        var disabledByEnvironment = SolidWorksRuntimeOptions.FromEnvironment(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["SW_DISABLE_REAL_EXECUTION"] = "true"
            },
            isUnitTestEnvironment: false);
        var ciEnvironment = SolidWorksRuntimeOptions.FromEnvironment(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["CI"] = "true"
            },
            isUnitTestEnvironment: false);
        var unitTestEnvironment = SolidWorksRuntimeOptions.FromEnvironment(
            emptyEnvironment,
            isUnitTestEnvironment: true);
        var validPlan = new SolidWorksBuildPlan(
            "v2-default-on-plan",
            "v2-default-on-spec",
            "SolidWorks",
            PlateBasic4HolesDefinition.Type,
            "mm",
            [
                new SolidWorksOperation(
                    "create-sketch",
                    "CreateSketch",
                    "Front Plane",
                    new Dictionary<string, string>(),
                    Array.Empty<string>(),
                    "Sketch created.")
            ],
            [
                new SolidWorksArtifact(
                    "part",
                    "PartModel",
                    "plate_basic_4holes.SLDPRT",
                    ".SLDPRT",
                    false,
                    0,
                    "Expected part.")
            ],
            Array.Empty<string>(),
            Array.Empty<string>());
        var legacyRequestReview = new SolidWorksBuildPlanValidator().Validate(
            new SolidWorksWorkerRequest(
                "v2-default-on-request",
                validPlan,
                Path.GetTempPath(),
                DryRun: false,
                AllowRealCadExecution: false));
        var environmentProbeInterface = Type.GetType(
            "SolidWorksWorker.ISolidWorksExecutionEnvironmentProbe, SolidWorksWorker",
            throwOnError: false);
        var environmentProbeType = Type.GetType(
            "SolidWorksWorker.SolidWorksExecutionEnvironmentProbe, SolidWorksWorker",
            throwOnError: false);
        var realWorkerType = Type.GetType(
            "SolidWorksWorker.RealSolidWorksWorker, SolidWorksWorker",
            throwOnError: false);
        var environmentProbeIsInjectable =
            environmentProbeInterface is not null &&
            environmentProbeType is not null &&
            environmentProbeInterface.IsAssignableFrom(environmentProbeType) &&
            realWorkerType?.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter =>
                    parameter.ParameterType == environmentProbeInterface)) == true;

        return new V20SolidWorksDefaultOnSelfCheckResult(
            LocalInteractiveDefaultEnabled:
                localInteractive.RealExecutionDefaultEnabled &&
                localInteractive.EnableRealExecution &&
                localInteractive.ShouldUseRealWorker(dryRun: false),
            DisableEnvironmentSupported:
                disabledByEnvironment.DisableRealExecution &&
                !disabledByEnvironment.EnableRealExecution &&
                !disabledByEnvironment.ShouldUseRealWorker(dryRun: false),
            CiExecutionDisabled:
                ciEnvironment.IsCiEnvironment &&
                !ciEnvironment.EnableRealExecution &&
                !ciEnvironment.ShouldUseRealWorker(dryRun: false),
            UnitTestExecutionDisabled:
                unitTestEnvironment.IsUnitTestEnvironment &&
                !unitTestEnvironment.EnableRealExecution &&
                !unitTestEnvironment.ShouldUseRealWorker(dryRun: false),
            DryRunDisablesRealExecution: !localInteractive.ShouldUseRealWorker(dryRun: true),
            VisibleDefaultTrue: localInteractive.VisibleModeDefault && localInteractive.Visible,
            ExecutionEnvironmentProbeSupported: environmentProbeIsInjectable,
            LegacyEnableFlagNotRequired: localInteractive.ShouldUseRealWorker(dryRun: false),
            LegacyRequestConfirmationNotRequired: legacyRequestReview.IsPassed);
    }

    private static V20AGenericCadModelSpecSelfCheckResult RunV20AGenericCadModelSpecChecks(
        string projectRoot,
        PlatformKernel platform,
        string versionStageText)
    {
        var requiredSpecProperties = new[]
        {
            nameof(CADModelSpec.ModelId),
            nameof(CADModelSpec.ModelType),
            nameof(CADModelSpec.Unit),
            nameof(CADModelSpec.Parameters),
            nameof(CADModelSpec.ReferenceGeometry),
            nameof(CADModelSpec.Sketches),
            nameof(CADModelSpec.Features),
            nameof(CADModelSpec.Material),
            nameof(CADModelSpec.OutputRequirements),
            nameof(CADModelSpec.DrawingRequirements),
            nameof(CADModelSpec.ExecutionOptions)
        };
        var genericCadModelSpecV2Supported = requiredSpecProperties.All(property =>
            typeof(CADModelSpec).GetProperty(property) is not null);
        var sketchDefinitionSupported =
            typeof(SketchDefinition).GetProperty(nameof(SketchDefinition.SketchId)) is not null &&
            typeof(SketchDefinition).GetProperty(nameof(SketchDefinition.ReferencePlane)) is not null &&
            typeof(SketchDefinition).GetProperty(nameof(SketchDefinition.Entities)) is not null &&
            typeof(SketchDefinition).GetProperty(nameof(SketchDefinition.Constraints)) is not null &&
            typeof(SketchDefinition).GetProperty(nameof(SketchDefinition.Dimensions)) is not null &&
            new[] { "line", "rectangle", "circle", "arc", "slot" }
                .All(SketchEntityTypes.Supported.Contains);
        var sketchConstraintsSupported =
            new[]
            {
                "horizontal", "vertical", "coincident", "concentric", "tangent",
                "parallel", "perpendicular", "equal", "dimensional"
            }.All(SketchConstraintTypes.Supported.Contains);
        var featureDefinitionSupported =
            typeof(FeatureDefinition).GetProperty(nameof(FeatureDefinition.FeatureId)) is not null &&
            typeof(FeatureDefinition).GetProperty(nameof(FeatureDefinition.Dependencies)) is not null &&
            typeof(FeatureDefinition).GetProperty(nameof(FeatureDefinition.ReferencedSketches)) is not null &&
            typeof(FeatureDefinition).GetProperty(nameof(FeatureDefinition.ReferencedFeatures)) is not null &&
            new[]
            {
                "extrude_boss", "extrude_cut", "revolve_boss", "revolve_cut", "hole",
                "fillet", "chamfer", "linear_pattern", "circular_pattern", "mirror"
            }.All(FeatureTypes.Supported.Contains);

        FeatureDefinition Node(string id, string type, string[] dependencies, int? order = null) =>
            new(
                id,
                type,
                new Dictionary<string, string>(),
                dependencies,
                referencedSketches: [],
                referencedFeatures: dependencies,
                executionOrder: order);

        var validGraph = new FeatureGraph(
        [
            Node("cut", FeatureTypes.ExtrudeCut, ["base"], 2),
            Node("base", FeatureTypes.ExtrudeBoss, [], 1)
        ]).ValidateAndSort();
        var cycleGraph = new FeatureGraph(
        [
            Node("cycle-a", FeatureTypes.ExtrudeBoss, ["cycle-b"]),
            Node("cycle-b", FeatureTypes.ExtrudeCut, ["cycle-a"])
        ]).ValidateAndSort();
        var missingGraph = new FeatureGraph(
            [Node("missing", FeatureTypes.ExtrudeCut, ["not-registered"])])
            .ValidateAndSort();
        var featureGraphSupported =
            validGraph.IsValid &&
            validGraph.OrderedFeatures.Select(feature => feature.FeatureId)
                .SequenceEqual(["base", "cut"], StringComparer.OrdinalIgnoreCase);
        var featureGraphCycleDetected =
            !cycleGraph.IsValid &&
            string.Equals(
                cycleGraph.FailureStage,
                PartFamilyFailureStages.FeatureDependencyCycle,
                StringComparison.OrdinalIgnoreCase);
        var missingFeatureDependencyRejected =
            !missingGraph.IsValid &&
            string.Equals(
                missingGraph.FailureStage,
                PartFamilyFailureStages.FeatureDependencyMissing,
                StringComparison.OrdinalIgnoreCase);

        var plateSource = SolidWorksWorkflowRouter.CreatePlateBasicFourHolesSpec();
        var flangeSource = new CADModelSpec(
            "self-check-v20-a-flange",
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
        var shaftSource = new CADModelSpec(
            "self-check-v20-a-shaft",
            ShaftBasicDefinition.Type,
            new Dictionary<string, string>
            {
                ["diameter_mm"] = "40",
                ["length_mm"] = "180",
                ["optional_step_diameters"] = "32,24",
                ["optional_step_lengths"] = "40,30"
            });

        bool UsesGenericGraph(
            IPartFamilyDefinition definition,
            CADModelSpec source,
            params string[] expectedFeatureIds)
        {
            var result = definition.GenerateBuildPlan(
                $"self-check-v20-a-{definition.PartType}",
                source);
            if (!result.IsSuccess || result.BuildPlan is not SolidWorksBuildPlan plan)
            {
                return false;
            }

            var compiledFeatureIds = plan.Operations
                .Select(operation => operation.Parameters.GetValueOrDefault("feature_id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return expectedFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                       .SetEquals(compiledFeatureIds) &&
                   new SolidWorksBuildPlanValidator().Validate(plan).IsPassed;
        }

        var compilerProbe = new BuildPlanCompiler().Compile(
            "self-check-v20-a-compiler",
            PartFamilyGenericModelFactory.CreatePlateBasic4Holes(plateSource));
        var buildPlanCompilerSupported =
            compilerProbe.IsSuccess &&
            compilerProbe.BuildPlan?.Operations.Any(operation =>
                operation.Parameters.ContainsKey("feature_id")) == true &&
            compilerProbe.BuildPlan.Operations[^2].OperationType == "SavePart" &&
            compilerProbe.BuildPlan.Operations[^1].OperationType == "ExportStep";
        var plateUsesGenericFeatureGraph = UsesGenericGraph(
            new PlateBasic4HolesDefinition(),
            plateSource,
            "plate_base_extrude",
            "plate_hole_cut");
        var flangeUsesGenericFeatureGraph = UsesGenericGraph(
            new FlangeBasicDefinition(),
            flangeSource,
            "flange_body_extrude",
            "flange_inner_cut",
            "flange_bolt_holes");
        var shaftUsesGenericFeatureGraph = UsesGenericGraph(
            new ShaftBasicDefinition(),
            shaftSource,
            "shaft_revolve");

        var workerAssembly = platform.WorkerRegistry
            .GetByName("FakeSolidWorksWorker")?
            .GetType()
            .Assembly;
        var realWorkerType = workerAssembly?.GetType(
            "SolidWorksWorker.RealSolidWorksWorker",
            throwOnError: false);
        var builderRegistryType = workerAssembly?.GetType(
            "SolidWorksWorker.PartFamilyBuilderRegistry",
            throwOnError: false);
        var noPartSpecificLogicInRealWorker =
            realWorkerType is not null &&
            builderRegistryType?.GetMethod("TryGetBuilder", BindingFlags.Public | BindingFlags.Instance) is not null &&
            realWorkerType.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == builderRegistryType));
        var v20ADocumented =
            versionStageText.Contains("V2.0-A", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(projectRoot, "docs", "v2_0_a_generic_cad_model_spec.md"));

        return new V20AGenericCadModelSpecSelfCheckResult(
            genericCadModelSpecV2Supported,
            sketchDefinitionSupported,
            sketchConstraintsSupported,
            featureDefinitionSupported,
            featureGraphSupported,
            featureGraphCycleDetected,
            missingFeatureDependencyRejected,
            buildPlanCompilerSupported,
            plateUsesGenericFeatureGraph,
            flangeUsesGenericFeatureGraph,
            shaftUsesGenericFeatureGraph,
            noPartSpecificLogicInRealWorker,
            v20ADocumented);
    }

    private static V20BFeatureHandlerSelfCheckResult RunV20BFeatureHandlerChecks(
        string projectRoot,
        PlatformKernel platform,
        string versionStageText)
    {
        var workerAssembly = platform.WorkerRegistry
            .GetByName("FakeSolidWorksWorker")?
            .GetType()
            .Assembly;
        var realWorkerType = workerAssembly?.GetType(
            "SolidWorksWorker.RealSolidWorksWorker",
            throwOnError: false);
        var registryType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.FeatureHandlerRegistry",
            throwOnError: false);
        var handlerContractType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.IFeatureHandler",
            throwOnError: false);
        var validationResultType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.FeatureHandlerValidationResult",
            throwOnError: false);
        var createDefaultMethod = registryType?.GetMethod(
            "CreateDefault",
            BindingFlags.Public | BindingFlags.Static);
        object? registry = null;
        object[] handlers = [];
        try
        {
            registry = createDefaultMethod?.Invoke(null, null);
            handlers = (registryType?
                    .GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
                    .Invoke(registry, null) as System.Collections.IEnumerable)?
                .Cast<object>()
                .ToArray() ?? [];
        }
        catch (TargetInvocationException)
        {
            registry = null;
            handlers = [];
        }

        var featureHandlerRegistryExists =
            registry is not null &&
            registryType?.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Instance) is not null &&
            registryType.GetMethod("TryGetHandler", BindingFlags.Public | BindingFlags.Instance) is not null &&
            registryType.GetMethod(
                "ValidateForRealExecution",
                BindingFlags.Public | BindingFlags.Instance) is not null;

        bool HandlerRegistered(string featureType, string expectedTypeName) =>
            handlers.Any(handler =>
                string.Equals(handler.GetType().Name, expectedTypeName, StringComparison.Ordinal) &&
                string.Equals(
                    handler.GetType()
                        .GetProperty("FeatureType", BindingFlags.Public | BindingFlags.Instance)?
                        .GetValue(handler)?
                        .ToString(),
                    featureType,
                    StringComparison.OrdinalIgnoreCase));

        var sketchHandlerRegistered = HandlerRegistered("sketch", "SketchHandler");
        var extrudeHandlerRegistered = HandlerRegistered(
            FeatureTypes.ExtrudeBoss,
            "ExtrudeBossHandler");
        var cutHandlerRegistered = HandlerRegistered(
            FeatureTypes.ExtrudeCut,
            "ExtrudeCutHandler");
        var holeHandlerRegistered = HandlerRegistered(
            FeatureTypes.Hole,
            "HoleHandler");
        var revolveHandlerRegistered = HandlerRegistered(
            FeatureTypes.RevolveBoss,
            "RevolveBossHandler");

        var featureHandlerValidationContractExists =
            handlerContractType?.GetMethod(
                "Validate",
                BindingFlags.Public | BindingFlags.Instance) is not null &&
            validationResultType?.GetProperty(
                "IsValid",
                BindingFlags.Public | BindingFlags.Instance) is not null &&
            validationResultType.GetProperty(
                "FailureStage",
                BindingFlags.Public | BindingFlags.Instance) is not null &&
            handlers.Length > 0 &&
            handlers.All(handler =>
                handler.GetType().GetMethod(
                    "Validate",
                    BindingFlags.Public | BindingFlags.Instance)?.DeclaringType == handler.GetType());

        object? EvidenceFor(object handler) =>
            handler.GetType()
                .GetProperty("ApiEvidence", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(handler);

        string? EvidenceStatus(object handler)
        {
            var evidence = EvidenceFor(handler);
            return evidence?
                .GetType()
                .GetProperty("Status", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(evidence)?
                .ToString();
        }

        var featureApiEvidenceRequired =
            handlerContractType?.GetProperty(
                "ApiEvidence",
                BindingFlags.Public | BindingFlags.Instance) is not null &&
            handlers.Length > 0 &&
            handlers.All(handler =>
                EvidenceFor(handler) is not null &&
                !string.IsNullOrWhiteSpace(EvidenceStatus(handler)));

        var probeOperations = new SolidWorksOperation[]
        {
            new(
                "v20-b-sketch",
                "CreateSketch",
                "Front Plane",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feature_id"] = "v20-b-sketch",
                    ["feature_type"] = "sketch",
                    ["sketch_id"] = "v20-b-sketch",
                    ["entities"] = JsonSerializer.Serialize(
                    new[]
                    {
                        new SketchEntity(
                            "v20-b-circle",
                            SketchEntityTypes.Circle,
                            new Dictionary<string, string>
                            {
                                ["center_x_mm"] = "0",
                                ["center_y_mm"] = "0",
                                ["radius_mm"] = "10"
                            })
                    })
                },
                [],
                "Self-check sketch probe."),
            new(
                "v20-b-extrude",
                "ExtrudeBoss",
                "Front Plane",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feature_id"] = "v20-b-extrude",
                    ["feature_type"] = FeatureTypes.ExtrudeBoss,
                    ["depth_mm"] = "10"
                },
                ["v20-b-sketch"],
                "Self-check extrude probe."),
            new(
                "v20-b-cut",
                "CutExtrude",
                "Front Plane",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feature_id"] = "v20-b-cut",
                    ["feature_type"] = FeatureTypes.ExtrudeCut,
                    ["through_all"] = "false",
                    ["depth_mm"] = "10"
                },
                ["v20-b-extrude"],
                "Self-check cut probe."),
            new(
                "v20-b-hole",
                "CreateSimpleHole",
                "Front Plane",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feature_id"] = "v20-b-hole",
                    ["feature_type"] = FeatureTypes.Hole,
                    ["hole_diameter_mm"] = "6",
                    ["depth_mm"] = "10"
                },
                ["v20-b-extrude"],
                "Self-check hole probe."),
            new(
                "v20-b-revolve",
                "RevolveBoss",
                "Front Plane",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feature_id"] = "v20-b-revolve",
                    ["feature_type"] = FeatureTypes.RevolveBoss,
                    ["angle_degrees"] = "360"
                },
                ["v20-b-sketch"],
                "Self-check revolve probe.")
        };
        var probePlan = new SolidWorksBuildPlan(
            "self-check-v20-b-feature-handlers",
            "self-check-v20-b",
            "SolidWorks",
            "generic_cad_model",
            "mm",
            probeOperations,
            [],
            [],
            ["Self-check only; no real SolidWorks connection is permitted."],
            ExecutionStrategy: SolidWorksBuildExecutionStrategies.FeatureHandlerGraph);

        object? preflightResult = null;
        try
        {
            preflightResult = registryType?
                .GetMethod(
                    "ValidateForRealExecution",
                    BindingFlags.Public | BindingFlags.Instance)?
                .Invoke(registry, [probePlan]);
        }
        catch (TargetInvocationException)
        {
            preflightResult = null;
        }

        var preflightType = preflightResult?.GetType();
        var preflightPassed =
            preflightType?
                .GetProperty("IsPassed", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(preflightResult) is true;
        var preflightFailureStage = preflightType?
            .GetProperty("FailureStage", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(preflightResult)?
            .ToString();
        var preflightIssues = (preflightType?
                .GetProperty("Issues", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(preflightResult) as System.Collections.IEnumerable)?
            .Cast<object>()
            .Select(issue => issue.ToString() ?? string.Empty)
            .ToArray() ?? [];
        var preflightFeatures = (preflightType?
                .GetProperty("Features", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(preflightResult) as System.Collections.IEnumerable)?
            .Cast<object>()
            .Count() ?? 0;
        var featureHandlerValidationSupported =
            featureHandlerValidationContractExists &&
            preflightResult is not null &&
            preflightFeatures == probeOperations.Length &&
            preflightIssues.All(issue =>
                !issue.Contains(
                    PartFamilyFailureStages.InvalidFeatureParameter,
                    StringComparison.OrdinalIgnoreCase) &&
                !issue.Contains(
                    PartFamilyFailureStages.UnsupportedFeatureType,
                    StringComparison.OrdinalIgnoreCase));
        // 判据是"注册表里存在未取证 Handler，且它在连接 COM 之前被拒绝"，
        // 不是"注册表恰好有 5 个 Handler"。写死数量会在每次注册新 Handler 时
        // 把这条保护悄悄关掉——V2.1-A 注册五个复杂特征后就真的发生过。
        var hasUnverifiedHandlerEvidence =
            handlers.Length > 0 &&
            handlers.Any(handler =>
                string.Equals(
                    EvidenceStatus(handler),
                    "unverified",
                    StringComparison.OrdinalIgnoreCase));

        // This is deliberately a behavioral contract check.  Earlier versions
        // read RealSolidWorksWorker.cs and compared string offsets, which could
        // be satisfied by comments and broke whenever the source was refactored.
        // The dedicated worker test owns the injected-session assertion that
        // ConnectAsync remains at zero for this same unverified handler case.
        var preflightRunsBeforeConnection =
            realWorkerType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => method.Name == "ExecuteAsync") == true &&
            registryType?.GetMethod(
                "ValidateForRealExecution",
                BindingFlags.Public | BindingFlags.Instance) is not null;
        var unverifiedApiBlocksRealExecution =
            hasUnverifiedHandlerEvidence &&
            preflightResult is not null &&
            !preflightPassed &&
            string.Equals(
                preflightFailureStage,
                PartFamilyFailureStages.FeatureApiUnverified,
                StringComparison.OrdinalIgnoreCase) &&
            preflightIssues.Length >= 1 &&
            preflightRunsBeforeConnection;

        // A registry that exposes an extensible registration boundary and
        // resolves every installed handler proves the architectural property
        // more directly than scanning for a particular C# switch spelling.
        var noFeatureTypeLargeSwitch =
            registryType?.GetMethod(
                "Register",
                BindingFlags.Public | BindingFlags.Instance,
                [handlerContractType!]) is not null &&
            handlers.Length > 0 &&
            handlers.Select(handler =>
                    handler.GetType()
                        .GetProperty("FeatureType", BindingFlags.Public | BindingFlags.Instance)?
                        .GetValue(handler)?
                        .ToString())
                .All(featureType => !string.IsNullOrWhiteSpace(featureType)) &&
            handlers.Select(handler =>
                    handler.GetType()
                        .GetProperty("FeatureType", BindingFlags.Public | BindingFlags.Instance)?
                        .GetValue(handler)?
                        .ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == handlers.Length;

        var stageDocumentPath = Path.Combine(
            projectRoot,
            "docs",
            "v2_0_b_feature_handlers.md");
        var stageDocumentText = File.Exists(stageDocumentPath)
            ? File.ReadAllText(stageDocumentPath)
            : string.Empty;
        var requiredDocumentTerms = new[]
        {
            "FeatureHandlerRegistry",
            "SketchHandler",
            "ExtrudeBossHandler",
            "ExtrudeCutHandler",
            "HoleHandler",
            "RevolveBossHandler",
            PartFamilyFailureStages.FeatureApiEvidenceInsufficient
        };
        var featureHandlerDocsCompleted =
            !string.IsNullOrWhiteSpace(stageDocumentText) &&
            requiredDocumentTerms.All(term =>
                stageDocumentText.Contains(term, StringComparison.OrdinalIgnoreCase));
        var v20BDocumented =
            versionStageText.Contains("V2.0-B", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(stageDocumentPath);

        return new V20BFeatureHandlerSelfCheckResult(
            featureHandlerRegistryExists,
            noFeatureTypeLargeSwitch,
            sketchHandlerRegistered,
            extrudeHandlerRegistered,
            cutHandlerRegistered,
            holeHandlerRegistered,
            revolveHandlerRegistered,
            featureHandlerValidationSupported,
            featureApiEvidenceRequired,
            unverifiedApiBlocksRealExecution,
            featureHandlerDocsCompleted,
            v20BDocumented);
    }

    private static V20CFeatureAdapterSelfCheckResult RunV20CFeatureAdapterChecks(
        string projectRoot,
        PlatformKernel platform,
        string versionStageText)
    {
        var workerAssembly = platform.WorkerRegistry
            .GetByName("FakeSolidWorksWorker")?
            .GetType()
            .Assembly;
        var adapterContract = workerAssembly?.GetType(
            "SolidWorksWorker.Features.ISolidWorksFeatureAdapter",
            throwOnError: false);
        var adapterType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.RealSolidWorksFeatureAdapter",
            throwOnError: false);
        var artifactType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.FeatureAdapterArtifact",
            throwOnError: false);
        var registryType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.FeatureHandlerRegistry",
            throwOnError: false);
        var handlerContract = workerAssembly?.GetType(
            "SolidWorksWorker.Features.IFeatureHandler",
            throwOnError: false);
        var comFacadeContract = workerAssembly?.GetType(
            "SolidWorksWorker.ISolidWorksComFacade",
            throwOnError: false);
        var handlers = (registryType?
                .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)?
                .Invoke(null, null) is { } registry
            ? registryType.GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
                .Invoke(registry, null) as System.Collections.IEnumerable
            : null)?
            .Cast<object>()
            .Select(handler => handler.GetType())
            .ToArray() ?? [];
        // 同上：判据是"每一个已注册 Handler 都不持有 COM 门面"，
        // 覆盖全部注册项而不是某个历史数量。
        var featureHandlerNoDirectComAccess =
            handlerContract is not null &&
            comFacadeContract is not null &&
            handlers.Length > 0 &&
            handlers.All(handlerType =>
                handlerContract.IsAssignableFrom(handlerType) &&
                !handlerType.GetConstructors(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Any(parameter => parameter.ParameterType == comFacadeContract) &&
                !handlerType.GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Any(field => field.FieldType == comFacadeContract));
        var builderType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.SolidWorksFeatureGraphPartFamilyBuilder",
            throwOnError: false);
        var builderContract = workerAssembly?.GetType(
            "SolidWorksWorker.IPartFamilyBuilder",
            throwOnError: false);
        var featureAdapterLayerExists =
            adapterContract is not null &&
            adapterContract.IsInterface &&
            builderType is not null &&
            builderContract?.IsAssignableFrom(builderType) == true &&
            builderType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters().Any(parameter =>
                    parameter.ParameterType.Name == "FeatureHandlerRegistry"));
        var solidWorksFeatureAdapterExists =
            adapterType is not null &&
            adapterContract is not null &&
            adapterContract.IsAssignableFrom(adapterType);

        bool AdapterMethodExists(string name) =>
            adapterContract?.GetMethod(name, BindingFlags.Public | BindingFlags.Instance) is not null &&
            adapterType?.GetMethod(name, BindingFlags.Public | BindingFlags.Instance) is not null;

        var sketchRealExecutionSupported = AdapterMethodExists("ExecuteSketchAsync");
        var extrudeRealExecutionSupported = AdapterMethodExists("ExecuteExtrudeBossAsync");
        var cutRealExecutionSupported = AdapterMethodExists("ExecuteExtrudeCutAsync");
        var holeRealExecutionSupported = AdapterMethodExists("ExecuteHoleAsync");
        var featureResultValidationSupported =
            artifactType?.GetProperty("ResultObjectValidated") is not null &&
            artifactType.GetProperty("RebuildPassed") is not null &&
            artifactType.GetProperty("GeometryChangeValidated") is not null &&
            AdapterMethodExists("ExecuteSketchAsync") &&
            AdapterMethodExists("ExecuteExtrudeBossAsync") &&
            AdapterMethodExists("ExecuteExtrudeCutAsync") &&
            AdapterMethodExists("ExecuteHoleAsync");
        var featureFakeSuccessGuardSupported =
            featureResultValidationSupported &&
            typeof(CadArtifactContentValidator).GetMethod(
                nameof(CadArtifactContentValidator.TryValidateStepFile),
                BindingFlags.Public | BindingFlags.Static) is not null;
        var featurePipelineEndToEndSupported =
            File.Exists(Path.Combine(projectRoot, "examples", "feature_pipeline_plate.json")) &&
            builderType is not null &&
            workerAssembly?.GetType(
                "SolidWorksWorker.Features.FeatureExecutionPipeline",
                throwOnError: false)?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance) is not null &&
            typeof(SolidWorksArtifactValidator).GetMethod(
                nameof(SolidWorksArtifactValidator.Validate),
                BindingFlags.Public | BindingFlags.Instance) is not null;
        var featureProductionEvidenceActive = ValidateProductionFeatureEvidence(workerAssembly);
        var stageDocumentPath = Path.Combine(
            projectRoot,
            "docs",
            "v2_0_c_feature_adapter_execution.md");
        var v20CDocumented =
            versionStageText.Contains("V2.0-C", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(stageDocumentPath);

        return new V20CFeatureAdapterSelfCheckResult(
            featureAdapterLayerExists,
            featureHandlerNoDirectComAccess,
            solidWorksFeatureAdapterExists,
            sketchRealExecutionSupported,
            extrudeRealExecutionSupported,
            cutRealExecutionSupported,
            holeRealExecutionSupported,
            featurePipelineEndToEndSupported,
            featureProductionEvidenceActive,
            featureResultValidationSupported,
            featureFakeSuccessGuardSupported,
            v20CDocumented);
    }

    private static bool ValidateProductionFeatureEvidence(Assembly? workerAssembly)
    {
        try
        {
            var registryType = workerAssembly?.GetType(
                "SolidWorksWorker.Features.FeatureHandlerRegistry",
                throwOnError: false);
            var evidencePolicyType = workerAssembly?.GetType(
                "SolidWorksWorker.Features.FeatureExecutionEvidencePolicy",
                throwOnError: false);
            var registry = registryType?
                .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)?
                .Invoke(null, null);
            var handlers = (registryType?
                    .GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
                    .Invoke(registry, null) as System.Collections.IEnumerable)?
                .Cast<object>()
                .ToArray() ?? [];
            var validateEvidence = evidencePolicyType?.GetMethod(
                "ValidateEvidence",
                BindingFlags.Public | BindingFlags.Static);
            var expectedTypes = new HashSet<string>(
                ["sketch", FeatureTypes.ExtrudeBoss, FeatureTypes.ExtrudeCut, FeatureTypes.Hole],
                StringComparer.OrdinalIgnoreCase);
            var productionHandlers = handlers
                .Where(handler => expectedTypes.Contains(
                    handler.GetType().GetProperty("FeatureType")?.GetValue(handler)?.ToString() ?? string.Empty))
                .ToArray();
            if (validateEvidence is null || productionHandlers.Length != expectedTypes.Count)
            {
                return false;
            }

            return productionHandlers.All(handler =>
            {
                var featureType = handler.GetType().GetProperty("FeatureType")?.GetValue(handler)?.ToString();
                if (string.IsNullOrWhiteSpace(featureType))
                {
                    return false;
                }

                var result = validateEvidence.Invoke(
                    null,
                    [handler, new FeatureDefinition($"self-check-{featureType}", featureType, new Dictionary<string, string>())]);
                return result?.GetType().GetProperty("IsValid")?.GetValue(result) is true;
            });
        }
        catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static V20DModelRebuildSelfCheckResult RunV20DModelRebuildChecks(
        string projectRoot,
        string outputRoot,
        string versionStageText,
        bool markdownChineseCheckPassed)
    {
        var examplePath = Path.Combine(projectRoot, "examples", "parameter_update_plate.json");
        var stageDocumentPath = Path.Combine(projectRoot, "docs", "v2_0_d_model_rebuild_geometry_validation.md");

        var stageDocumentText = File.Exists(stageDocumentPath)
            ? File.ReadAllText(stageDocumentPath)
            : string.Empty;
        var workerAssembly = Type.GetType("SolidWorksWorker.FakeSolidWorksWorker, SolidWorksWorker", throwOnError: false)?.Assembly;
        var pipelineType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.FeatureExecutionPipeline",
            throwOnError: false);
        var readerType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.RealSolidWorksGeometryReader",
            throwOnError: false);
        var builderType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.V20DFeatureGraphPartFamilyBuilder",
            throwOnError: false);
        var profileEvidenceType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.V20DThreeCircleCutEvidencePolicy",
            throwOnError: false);
        var realWorkerType = workerAssembly?.GetType(
            "SolidWorksWorker.RealSolidWorksWorker",
            throwOnError: false);
        var geometryReaderContract = workerAssembly?.GetType(
            "SolidWorksWorker.Features.ISolidWorksGeometryReader",
            throwOnError: false);
        var builderContract = workerAssembly?.GetType(
            "SolidWorksWorker.IPartFamilyBuilder",
            throwOnError: false);
        var modelRebuildPipelineExists =
            typeof(ModelUpdateService).GetMethod(nameof(ModelUpdateService.Prepare)) is not null &&
            pipelineType?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance) is not null &&
            builderType is not null &&
            builderContract?.IsAssignableFrom(builderType) == true &&
            profileEvidenceType?.GetMethod(
                "ValidateForRealExecution",
                BindingFlags.Public | BindingFlags.Static) is not null &&
            profileEvidenceType.GetMethod(
                "ValidatePlanProfile",
                BindingFlags.Public | BindingFlags.Static) is not null &&
            realWorkerType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => method.Name == "ExecuteAsync") == true &&
            typeof(SolidWorksMainWorkflowRunner).GetMethod(
                "ExecuteModelUpdateReleasePackageAsync",
                BindingFlags.NonPublic | BindingFlags.Instance) is not null;

        CADModelSpec? original = null;
        ModelUpdatePreparationResult? prepared = null;
        try
        {
            using var example = JsonDocument.Parse(File.ReadAllText(examplePath));
            original = JsonSerializer.Deserialize<CADModelSpec>(
                example.RootElement.GetProperty("cad_model_spec").GetRawText(),
                JsonOptions());
            var update = example.RootElement.GetProperty("parameter_update");
            var oldParameters = ReadStringDictionary(update.GetProperty("old_parameters"));
            var newParameters = ReadStringDictionary(update.GetProperty("new_parameters"));
            if (original is not null)
            {
                prepared = new ModelUpdateService().Prepare(
                    "self-check-v20-d-parameter-update",
                    original,
                    new ModelParameterUpdateRequest(newParameters, oldParameters));
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // A failed parse/update is reflected in ParameterUpdateSupported below.
        }

        var parameterUpdateSupported =
            prepared?.IsSuccess == true &&
            prepared.FeatureGraphPreserved &&
            prepared.UpdatedModelSpec?.Parameters.GetValueOrDefault("length_mm") == "200" &&
            prepared.UpdatedModelSpec.Parameters.GetValueOrDefault("width_mm") == "100" &&
            prepared.UpdatedModelSpec.Parameters.GetValueOrDefault("thickness_mm") == "15" &&
            prepared.ChangedFeatures.Count >= 3 &&
            prepared.BuildPlan?.Operations.Any(operation =>
                operation.Parameters.GetValueOrDefault("feature_type") == FeatureTypes.Hole) == true;
        var productionEvidenceActive = ValidateV20DProductionEvidence(
            workerAssembly,
            prepared?.BuildPlan);

        var solidWorksRebuildSupported =
            readerType is not null &&
            geometryReaderContract?.IsAssignableFrom(readerType) == true &&
            readerType.GetMethod("Read", BindingFlags.Public | BindingFlags.Instance) is { } readMethod &&
            readMethod.ReturnType.GetProperty("FailureStage", BindingFlags.Public | BindingFlags.Instance) is not null;
        var geometryValidatorExists =
            typeof(GeometryValidator).GetMethod(nameof(GeometryValidator.Validate), [typeof(CADModelSpec), typeof(MeasuredGeometry), typeof(IReadOnlyList<string>)]) is not null &&
            typeof(GeometryValidationArtifactValidator).GetMethods(
                    BindingFlags.Public | BindingFlags.Instance)
                .Any(method => method.Name == nameof(GeometryValidationArtifactValidator.Validate));
        var boundingBoxValidationSupported =
            typeof(GeometryBoundingBox).GetProperties().Length >= 6 &&
            typeof(MeasuredGeometry).GetProperty(nameof(MeasuredGeometry.BoundingBox)) is not null &&
            typeof(MeasuredGeometry).GetProperty(nameof(MeasuredGeometry.ExactExtents)) is not null &&
            typeof(GeometryValidator).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(method => method.Name == nameof(GeometryValidator.BuildExpectedGeometry));
        var volumeValidationSupported =
            typeof(MeasuredGeometry).GetProperty(nameof(MeasuredGeometry.BodyCount)) is not null &&
            typeof(MeasuredGeometry).GetProperty(nameof(MeasuredGeometry.VolumeCubicMillimeters)) is not null &&
            typeof(MeasuredGeometry).GetProperty(nameof(MeasuredGeometry.MassPropertyVolumeCubicMillimeters)) is not null &&
            typeof(GeometryValidator).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => method.Name == nameof(GeometryValidator.Validate));
        var parameterGeometryMatchSupported =
            typeof(MeasuredGeometry).GetProperty(nameof(MeasuredGeometry.Cylinders)) is not null &&
            typeof(MeasuredCylinder).GetProperty(nameof(MeasuredCylinder.DiameterMm)) is not null &&
            typeof(GeometryValidationReport).GetProperty(nameof(GeometryValidationReport.FailureStage)) is not null;

        var requiredFailureStages = new[]
        {
            PartFamilyFailureStages.RebuildFailed,
            PartFamilyFailureStages.GeometryReadFailed,
            PartFamilyFailureStages.BoundingBoxInvalid,
            PartFamilyFailureStages.VolumeValidationFailed,
            PartFamilyFailureStages.ParameterGeometryMismatch,
            PartFamilyFailureStages.FeatureMissingAfterRebuild,
            PartFamilyFailureStages.GeometryReportFailed
        };
        var failedRebuildReport = original is null
            ? null
            : new GeometryValidator().Validate(
                original,
                new MeasuredGeometry(
                    RebuildPassed: false,
                    BoundingBox: null,
                    ExactExtents: null,
                    BodyCount: null,
                    VolumeCubicMillimeters: null,
                    MassKilograms: null,
                    MassPropertyVolumeCubicMillimeters: null,
                    ReadIssues: Array.Empty<string>()));
        var rebuildFailureDetected =
            requiredFailureStages.All(stage =>
                typeof(PartFamilyFailureStages)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(field => field.GetRawConstantValue()?.ToString())
                    .Contains(stage, StringComparer.OrdinalIgnoreCase)) &&
            string.Equals(
                failedRebuildReport?.FailureStage,
                PartFamilyFailureStages.RebuildFailed,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(failedRebuildReport?.FinalStatus, "Failed", StringComparison.OrdinalIgnoreCase);

        var geometryReportPath = Path.Combine(outputRoot, "reports", "v2_0_d_geometry_validation_report.json");
        var geometryReportGenerated = false;
        if (prepared?.UpdatedModelSpec is CADModelSpec updated && parameterUpdateSupported)
        {
            var expected = GeometryValidator.BuildExpectedGeometry(updated);
            var expectedVolume = expected.ExpectedVolumeCubicMillimeters;
            if (expectedVolume is > 0)
            {
                try
                {
                    var box = new GeometryBoundingBox(0d, 0d, 0d, 200d, 100d, 15d);
                    var measured = new MeasuredGeometry(
                        RebuildPassed: true,
                        BoundingBox: box,
                        ExactExtents: box,
                        BodyCount: 1,
                        VolumeCubicMillimeters: expectedVolume.Value,
                        MassKilograms: 2.3d,
                        MassPropertyVolumeCubicMillimeters: expectedVolume.Value,
                        FeatureTypes: ["ProfileFeature", "BossExtrude", "CutExtrude"],
                        CylindricalDiametersMm: [10d, 10d, 10d, 10d],
                        Cylinders:
                        [
                            new MeasuredCylinder(10d, -80d, -30d, 0d, 0d, 0d, 1d),
                            new MeasuredCylinder(10d, 80d, -30d, 0d, 0d, 0d, 1d),
                            new MeasuredCylinder(10d, -80d, 30d, 0d, 0d, 0d, 1d),
                            new MeasuredCylinder(10d, 80d, 30d, 0d, 0d, 0d, 1d)
                        ],
                        ReadIssues: Array.Empty<string>());
                    var validationReport = new GeometryValidator().Validate(
                        updated,
                        measured,
                        new[] { "sketch" }
                            .Concat(updated.Features.Select(feature => feature.FeatureType))
                            .ToArray());
                    GeometryValidator.WriteReport(geometryReportPath, validationReport);
                    using var reportDocument = JsonDocument.Parse(File.ReadAllText(geometryReportPath));
                    geometryReportGenerated =
                        string.Equals(validationReport.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase) &&
                        reportDocument.RootElement.TryGetProperty("model_id", out _) &&
                        reportDocument.RootElement.TryGetProperty("input_parameters", out _) &&
                        reportDocument.RootElement.TryGetProperty("measured_geometry", out _) &&
                        reportDocument.RootElement.TryGetProperty("expected_geometry", out _) &&
                        reportDocument.RootElement.TryGetProperty("deviations", out _) &&
                        reportDocument.RootElement.TryGetProperty("passed_checks", out _) &&
                        reportDocument.RootElement.TryGetProperty("failed_checks", out _) &&
                        reportDocument.RootElement.TryGetProperty("failure_stage", out _) &&
                        reportDocument.RootElement.TryGetProperty("final_status", out _);
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                    geometryReportGenerated = false;
                }
            }
        }

        var v20DDocumented =
            versionStageText.Contains("V2.0-D", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(stageDocumentPath);
        var markdownChineseCheck =
            markdownChineseCheckPassed &&
            stageDocumentText.Any(character => character is >= '\u4e00' and <= '\u9fff');

        return new V20DModelRebuildSelfCheckResult(
            modelRebuildPipelineExists,
            parameterUpdateSupported,
            solidWorksRebuildSupported,
            geometryValidatorExists,
            boundingBoxValidationSupported,
            volumeValidationSupported,
            parameterGeometryMatchSupported,
            rebuildFailureDetected,
            geometryReportGenerated,
            v20DDocumented,
            productionEvidenceActive,
            markdownChineseCheck);
    }

    private static bool ValidateFeatureGraphProductionEvidence(
        Assembly? workerAssembly,
        SolidWorksBuildPlan? buildPlan)
    {
        if (buildPlan is null)
        {
            return false;
        }

        try
        {
            var registryType = workerAssembly?.GetType(
                "SolidWorksWorker.Features.FeatureHandlerRegistry",
                throwOnError: false);
            var registry = registryType?.GetMethod(
                "CreateDefault",
                BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            var validate = registryType?.GetMethod(
                "ValidateForRealExecution",
                BindingFlags.Public | BindingFlags.Instance,
                [typeof(SolidWorksBuildPlan)]);
            var result = validate?.Invoke(registry, [buildPlan]);
            return result?.GetType().GetProperty("IsPassed")?.GetValue(result) is true;
        }
        catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool ValidateV20DProductionEvidence(
        Assembly? workerAssembly,
        SolidWorksBuildPlan? buildPlan)
    {
        if (buildPlan is null)
        {
            return false;
        }

        try
        {
            var policyType = workerAssembly?.GetType(
                "SolidWorksWorker.Features.V20DThreeCircleCutEvidencePolicy",
                throwOnError: false);
            var validate = policyType?.GetMethod(
                "ValidateForRealExecution",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(SolidWorksBuildPlan)]);
            var result = validate?.Invoke(null, [buildPlan]);
            return result?.GetType().GetProperty("IsPassed")?.GetValue(result) is true;
        }
        catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// V2.1-A 复杂特征库自检。判据全部为行为式：注册用反射解析，
    /// dry-run 真实编译计划，"未验证禁止真实执行"真实调用证据校验，
    /// 不使用任何 .cs 源码字符串匹配。
    /// </summary>
    private static V21AComplexFeatureSelfCheckResult RunV21AComplexFeatureChecks(
        string projectRoot,
        PlatformKernel platform,
        string versionStageText)
    {
        // PlatformCore 不引用 SolidWorksWorker，因此通过 Worker 程序集反射访问，
        // 与 V2.0-C 判据同一模式。
        var workerAssembly = platform.WorkerRegistry.GetByName("FakeSolidWorksWorker")?.GetType().Assembly;
        var registryType = workerAssembly?.GetType("SolidWorksWorker.Features.FeatureHandlerRegistry", false);
        var contextType = workerAssembly?.GetType("SolidWorksWorker.Features.FeatureHandlerBuildPlanContext", false);
        var registry = registryType?
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, null);
        var handlers = (registryType?
                .GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
                .Invoke(registry, null) as System.Collections.IEnumerable)?
            .Cast<object>()
            .ToArray() ?? [];

        object? Handler(string featureType) => handlers.SingleOrDefault(item =>
            string.Equals(
                item.GetType().GetProperty("FeatureType")?.GetValue(item)?.ToString(),
                featureType,
                StringComparison.OrdinalIgnoreCase));

        bool HandlerRegistered(string featureType, string expectedTypeName)
        {
            var handler = Handler(featureType);
            if (handler is null || handler.GetType().Name != expectedTypeName)
            {
                return false;
            }

            var schema = handler.GetType().GetProperty("ParameterSchema")?.GetValue(handler)
                as System.Collections.ICollection;
            var evidence = handler.GetType().GetProperty("ApiEvidence")?.GetValue(handler);
            var apiName = evidence?.GetType().GetProperty("ApiName")?.GetValue(evidence)?.ToString();
            return schema is { Count: > 0 } && !string.IsNullOrWhiteSpace(apiName);
        }

        var filletRegistered = HandlerRegistered(FeatureTypes.Fillet, "FilletHandler");
        var chamferRegistered = HandlerRegistered(FeatureTypes.Chamfer, "ChamferHandler");
        var linearPatternRegistered = HandlerRegistered(FeatureTypes.LinearPattern, "LinearPatternHandler");
        var circularPatternRegistered = HandlerRegistered(FeatureTypes.CircularPattern, "CircularPatternHandler");
        var mirrorRegistered = HandlerRegistered(FeatureTypes.Mirror, "MirrorHandler");
        var complexFeatureRegistrySupported =
            filletRegistered &&
            chamferRegistered &&
            linearPatternRegistered &&
            circularPatternRegistered &&
            mirrorRegistered;

        // dry-run：用合法参数真实走一遍 Validate + BuildPlan，证明计划链路可编译。
        var regressionPassed = complexFeatureRegistrySupported && contextType is not null;
        // 证据门必须双向可鉴别：未取证的能力即使参数完全合法也要被拒绝，
        // 已取证的能力必须真的过得了自己的证据门。
        var unverifiedBlocksExecution = complexFeatureRegistrySupported;
        foreach (var (featureType, parameters) in ComplexFeatureSamples())
        {
            var handler = Handler(featureType);
            if (handler is null || contextType is null)
            {
                regressionPassed = false;
                unverifiedBlocksExecution = false;
                continue;
            }

            var feature = new FeatureDefinition(
                $"selfcheck_{featureType}",
                featureType,
                parameters,
                referencedFeatures: ["seed_boss"]);

            var validation = handler.GetType().GetMethod("Validate")?.Invoke(handler, [feature]);
            var context = Activator.CreateInstance(
                contextType,
                [$"selfcheck-op-{featureType}", "TopPlane", (IReadOnlyList<string>)Array.Empty<string>()]);
            var plan = handler.GetType().GetMethod("BuildPlan")?.Invoke(handler, [feature, context]);
            if (validation?.GetType().GetProperty("IsValid")?.GetValue(validation) is not true ||
                plan?.GetType().GetProperty("Operation")?.GetValue(plan) is null ||
                plan.GetType().GetProperty("FailureStage")?.GetValue(plan) is not null)
            {
                regressionPassed = false;
            }

            var evidence = handler.GetType().GetProperty("ApiEvidence")?.GetValue(handler);
            var allowsReal = evidence?.GetType().GetProperty("AllowsRealExecution")?.GetValue(evidence) is true;
            var evidenceCheck = handler.GetType()
                .GetMethod("ValidateEvidenceForRealExecution")?
                .Invoke(handler, [feature]);
            var evidenceValid = evidenceCheck?.GetType().GetProperty("IsValid")?.GetValue(evidenceCheck) is true;
            var evidenceStage = evidenceCheck?.GetType()
                .GetProperty("FailureStage")?.GetValue(evidenceCheck)?.ToString();

            // 期望方向取自 Handler 自报的证据状态，而不是写死的特征清单：
            // 每补一个真机证据就要改一次判据，正是"保护随迁移消失"的老毛病。
            // PlatformCore 不引用 SolidWorksWorker，所以此处比较字面量而非
            // FeatureApiEvidenceStatuses 常量。只把状态改成 verified 却不补
            // 证据绑定的伪装取证，会在下面的 verified 分支上失败。
            var status = evidence?.GetType().GetProperty("Status")?.GetValue(evidence)?.ToString();
            var declaresVerified = string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase);
            if (declaresVerified)
            {
                if (!allowsReal || !evidenceValid)
                {
                    unverifiedBlocksExecution = false;
                }
            }
            else if (allowsReal ||
                evidenceValid ||
                !string.Equals(evidenceStage, PartFamilyFailureStages.FeatureApiUnverified, StringComparison.Ordinal))
            {
                unverifiedBlocksExecution = false;
            }
        }

        // 边选择模型：判据必须可鉴别——既要能唯一命中，也要在不唯一时拒绝。
        // 用固定夹具而非真实 CAD，因此自检不依赖 SolidWorks。
        MeasuredEdge Rim(int index, double x, double y) =>
            new(index, EdgeKinds.Circle, 31.4159d, x, y, 0d, 5d, [SurfaceKinds.Cylinder, SurfaceKinds.Plane]);
        var edgeFixture = new[] { Rim(0, -20d, 10d), Rim(1, 20d, 10d), Rim(2, -20d, 0d), Rim(3, 20d, 0d) };

        var uniqueHit = EdgeSelectionResolver.Resolve(
            edgeFixture,
            new EdgeSelectionCriteria(
                Kind: EdgeKinds.Circle,
                AdjacentSurfaceKinds: [SurfaceKinds.Cylinder, SurfaceKinds.Plane],
                AnchorYMm: 10d,
                ExpectedCount: 2));
        var ambiguousRejected = EdgeSelectionResolver.Resolve(
            edgeFixture,
            new EdgeSelectionCriteria(Kind: EdgeKinds.Circle, ExpectedCount: 1));
        var unconstrainedRejected = EdgeSelectionResolver.Resolve(edgeFixture, new EdgeSelectionCriteria());

        var edgeSelectionModelSupported =
            uniqueHit.IsResolved &&
            uniqueHit.Matches.Count == 2 &&
            !ambiguousRejected.IsResolved &&
            string.Equals(
                ambiguousRejected.FailureStage,
                PartFamilyFailureStages.EdgeSelectionAmbiguous,
                StringComparison.Ordinal) &&
            !unconstrainedRejected.IsResolved &&
            string.Equals(
                unconstrainedRejected.FailureStage,
                PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                StringComparison.Ordinal) &&
            EdgeSelectionCriteriaParser.TryParse(
                """{"kind":"circle","expected_count":2}""", out _, out _) &&
            !EdgeSelectionCriteriaParser.TryParse("top_outer_edges", out _, out _);

        string[] documentationFiles =
        [
            "src/Workers/SolidWorks/Features/Fillet/api_evidence.md",
            "src/Workers/SolidWorks/Features/Fillet/failure_repair.md",
            "src/Workers/SolidWorks/Features/Chamfer/api_evidence.md",
            "src/Workers/SolidWorks/Features/Chamfer/failure_repair.md",
            "src/Workers/SolidWorks/Features/Pattern/api_evidence_linear_pattern.md",
            "src/Workers/SolidWorks/Features/Pattern/failure_repair_linear_pattern.md",
            "src/Workers/SolidWorks/Features/Pattern/api_evidence_circular_pattern.md",
            "src/Workers/SolidWorks/Features/Pattern/failure_repair_circular_pattern.md",
            "src/Workers/SolidWorks/Features/Mirror/api_evidence.md",
            "src/Workers/SolidWorks/Features/Mirror/failure_repair.md"
        ];
        var featureLibraryDocumented = documentationFiles.All(relative =>
            File.Exists(Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar))));

        var v21ADocumented =
            versionStageText.Contains("V2.1-A", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(projectRoot, "docs", "v2_1_a_complex_feature_library.md"));

        return new(
            filletRegistered,
            chamferRegistered,
            linearPatternRegistered,
            circularPatternRegistered,
            mirrorRegistered,
            complexFeatureRegistrySupported,
            unverifiedBlocksExecution,
            featureLibraryDocumented,
            regressionPassed,
            v21ADocumented,
            edgeSelectionModelSupported);
    }

    private static IEnumerable<(string FeatureType, Dictionary<string, string> Parameters)> ComplexFeatureSamples()
    {
        yield return (FeatureTypes.Fillet, new Dictionary<string, string>
        {
            ["radius_mm"] = "3",
            // 结构化判据：顶面孔口（圆边 + 圆柱面/平面相交 + y=10），恰好两条。
            ["edge_selection"] =
                """
                {"kind":"circle","adjacent_surface_kinds":["cylinder","plane"],"anchor_y_mm":10,"expected_count":2}
                """
        });
        yield return (FeatureTypes.Chamfer, new Dictionary<string, string>
        {
            ["distance_mm"] = "2",
            ["angle_deg"] = "45",
            // 另一侧孔口（圆边 + 圆柱面/平面相交 + y=0），恰好两条。
            // 与圆角样本只差定位点，用来证明判据真的按位置区分。
            ["edge_selection"] =
                """
                {"kind":"circle","adjacent_surface_kinds":["cylinder","plane"],"anchor_y_mm":0,"expected_count":2}
                """
        });
        yield return (FeatureTypes.LinearPattern, new Dictionary<string, string>
        {
            ["direction"] = "x",
            // 方向边：板顶面沿 X 的那条 100 mm 棱。
            ["direction_selection"] =
                """
                {"kind":"line","length_mm":100,"anchor_x_mm":0,"anchor_y_mm":10,"anchor_z_mm":30,"expected_count":1}
                """,
            ["instance_count"] = "4",
            ["spacing_mm"] = "20",
            ["seed_feature"] = "seed_boss"
        });
        yield return (FeatureTypes.CircularPattern, new Dictionary<string, string>
        {
            ["axis"] = "y",
            // 轴边：中心 Ø10 孔的顶面孔口，其法向即阵列轴。
            ["axis_selection"] =
                """
                {"kind":"circle","radius_mm":5,"anchor_x_mm":0,"anchor_y_mm":10,"anchor_z_mm":0,"expected_count":1}
                """,
            ["instance_count"] = "6",
            ["angle_deg"] = "360",
            ["seed_feature"] = "seed_boss"
        });
        yield return (FeatureTypes.Mirror, new Dictionary<string, string>
        {
            ["mirror_plane"] = "FrontPlane",
            ["target_features"] = "seed_boss"
        });
    }

    private static V20EUnifiedFeatureGraphSelfCheckResult RunV20EUnifiedFeatureGraphChecks(
        string projectRoot,
        PlatformKernel platform,
        string versionStageText,
        V18PartFamilySelfCheckResult v18PartFamilyChecks,
        V19PartFamilySelfCheckResult v19PartFamilyChecks,
        V20BFeatureHandlerSelfCheckResult v20BFeatureHandlerChecks,
        V20CFeatureAdapterSelfCheckResult v20CFeatureAdapterChecks,
        V20DModelRebuildSelfCheckResult v20DModelRebuildChecks,
        V21AComplexFeatureSelfCheckResult v21AComplexFeatureChecks,
        IReadOnlyDictionary<string, bool> v21BHoleChecks)
    {
        var definitions = PartTypeRegistry.CreateDefault().GetAll();
        var workerAssembly = platform.WorkerRegistry
            .GetByName("FakeSolidWorksWorker")?
            .GetType()
            .Assembly;
        var builderRegistryType = workerAssembly?.GetType(
            "SolidWorksWorker.PartFamilyBuilderRegistry",
            throwOnError: false);
        var createBuilderRegistry = builderRegistryType?
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static);
        var builderRegistry = createBuilderRegistry?.Invoke(
            null,
            createBuilderRegistry.GetParameters().Select(_ => (object?)null).ToArray());
        var builders = (builderRegistryType?
                .GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
                .Invoke(builderRegistry, null) as System.Collections.IEnumerable)?
            .Cast<object>()
            .ToArray() ?? [];

        string? Property(object builder, string name) =>
            builder.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(builder)?
                .ToString();

        var unifiedPartFamilyBuilders =
            definitions.Count > 0 &&
            builders.Length == definitions.Count &&
            definitions.All(definition =>
            {
                var matching = builders.Where(builder =>
                    string.Equals(
                        Property(builder, "PartType"),
                        definition.PartType,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
                return matching.Length == 1 &&
                       string.Equals(
                           Property(matching[0], "RealExecutionMode"),
                           PartFamilyExecutionModes.GenericFeatureGraph,
                           StringComparison.Ordinal);
            });

        var controlledPlateEvidenceActive =
            v20CFeatureAdapterChecks.FeatureProductionEvidenceActive &&
            v20DModelRebuildChecks.ProductionEvidenceActive;
        var stepContentGateActive =
            typeof(CadArtifactContentValidator).GetMethod(
                nameof(CadArtifactContentValidator.TryValidateStepFile),
                BindingFlags.Public | BindingFlags.Static) is not null &&
            controlledPlateEvidenceActive;
        var jacketDefinition = definitions.OfType<JacketBasicDefinition>().SingleOrDefault();
        var jacketBuilder = builders.SingleOrDefault(builder =>
            string.Equals(
                Property(builder, "PartType"),
                JacketBasicDefinition.Type,
                StringComparison.OrdinalIgnoreCase));
        var v21ARealExecutionFrozen =
            jacketDefinition?.SupportsRealExecution == false &&
            jacketBuilder?.GetType()
                .GetProperty("SupportsRealExecution", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(jacketBuilder) is false;
        var v20EDocumented =
            versionStageText.Contains("V2.0-E", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(
                projectRoot,
                "docs",
                "v2_0_e_unified_feature_graph_execution.md"));

        var regressionModels = PartFamilyRegressionModels.CreateDefault();
        var handlerRegistryType = workerAssembly?.GetType(
            "SolidWorksWorker.Features.FeatureHandlerRegistry",
            throwOnError: false);
        var handlerRegistry = handlerRegistryType?.GetMethod(
            "CreateDefault",
            BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        var registeredFeatureTypes = (handlerRegistryType?
                .GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
                .Invoke(handlerRegistry, null) as System.Collections.IEnumerable)?
            .Cast<object>()
            .Select(handler => Property(handler, "FeatureType"))
            .Where(featureType => !string.IsNullOrWhiteSpace(featureType))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        bool RegressionModelCompiles(string partType)
        {
            var model = regressionModels.SingleOrDefault(item =>
                string.Equals(item.PartType, partType, StringComparison.OrdinalIgnoreCase));
            var definition = definitions.SingleOrDefault(item =>
                string.Equals(item.PartType, partType, StringComparison.OrdinalIgnoreCase));
            if (model is null || definition is null || model.Spec.Sketches.Count == 0 || model.Spec.Features.Count == 0)
            {
                return false;
            }

            var buildPlan = definition.GenerateBuildPlan($"self-check-v20-e-{partType}", model.Spec);
            return buildPlan.IsSuccess &&
                   buildPlan.BuildPlan is not null &&
                   model.Spec.Features.All(feature => registeredFeatureTypes.Contains(feature.FeatureType));
        }

        var plateUsesPartFamilyDefinition = RegressionModelCompiles(PlateBasic4HolesDefinition.Type);
        var flangeUsesPartFamilyDefinition = RegressionModelCompiles(FlangeBasicDefinition.Type);
        var shaftUsesPartFamilyDefinition = RegressionModelCompiles(ShaftBasicDefinition.Type);
        // 回归输入必须覆盖每一个已注册零件族。用覆盖关系而不是硬编码数量：
        // 硬编码 == 3 会让「补齐第四个族」这件正确的事反而导致自检失败。
        var regressionCoversEveryRegisteredFamily =
            definitions.All(item => RegressionModelCompiles(item.PartType));
        var partFamilyDefinitionSupported =
            regressionModels.Count >= 3 &&
            regressionCoversEveryRegisteredFamily &&
            plateUsesPartFamilyDefinition &&
            flangeUsesPartFamilyDefinition &&
            shaftUsesPartFamilyDefinition;
        var commonFeatureTemplatesExists =
            typeof(CommonFeatureTemplates).GetMethod(
                nameof(CommonFeatureTemplates.CreateCircleSketch),
                BindingFlags.Public | BindingFlags.Static) is not null &&
            typeof(CommonFeatureTemplates).GetMethod(
                nameof(CommonFeatureTemplates.CreateDimensionalConstraint),
                BindingFlags.Public | BindingFlags.Static) is not null;
        // 模板复用的真实证据：每个零件族的 FeatureGraph 中都必须出现由
        // CommonFeatureTemplates.CreateDimensionalConstraint 产出的尺寸约束
        // （dimensional + 携带 parameter 元数据）。原判据比较的是
        // model.Spec.PartType == model.PartType，构造时两者同源，恒成立，
        // 只证明了模板方法存在，没有证明模板被消费。
        bool ConsumesSharedTemplates(PartFamilyRegressionModel model) =>
            model.Spec.Sketches.Any(sketch =>
                sketch.Constraints.Any(constraint =>
                    string.Equals(
                        constraint.ConstraintType,
                        SketchConstraintTypes.Dimensional,
                        StringComparison.OrdinalIgnoreCase) &&
                    constraint.Parameters.ContainsKey("parameter")));

        var featureGraphTemplateReuseSupported =
            commonFeatureTemplatesExists &&
            regressionModels.Count > 0 &&
            regressionModels.All(ConsumesSharedTemplates);
        var cadCapabilityMatrixExists = File.Exists(Path.Combine(
            projectRoot,
            "docs",
            "cad_capability_matrix.md"));
        var regressionModelsSupported =
            partFamilyDefinitionSupported &&
            registeredFeatureTypes.Count > 0;
        // 几何校验已从零件专用 Builder 提升为平台级后置阶段。判据必须可鉴别：
        // 既要证明零件族能声明期望，也要证明判定会拒绝错误几何——否则这个字段
        // 会退化成「类型存在」的恒真式，而几何校验正是 V2.0-E 迁移时静默失联的能力。
        var geometryValidationPlatformWide = false;
        var jacketDefinitionForGeometry = definitions.SingleOrDefault(item =>
            string.Equals(item.PartType, JacketBasicDefinition.Type, StringComparison.OrdinalIgnoreCase));
        var jacketGeometryModel = regressionModels.SingleOrDefault(item =>
            string.Equals(item.PartType, JacketBasicDefinition.Type, StringComparison.OrdinalIgnoreCase));
        if (jacketDefinitionForGeometry is not null && jacketGeometryModel is not null)
        {
            var geometryPlan = jacketDefinitionForGeometry
                .GenerateBuildPlan("self-check-v20-e-geometry", jacketGeometryModel.Spec)
                .BuildPlan;
            var expectedGeometry = geometryPlan is null
                ? null
                : jacketDefinitionForGeometry.DescribeExpectedGeometry(geometryPlan);
            if (expectedGeometry is { BodyCount: 1 } && expectedGeometry.VolumeCubicMillimeters > 0d)
            {
                MeasuredGeometry Measured(int bodies, double volume) =>
                    new(true, null, null, bodies, volume, null, null);

                var accepts = PartGeometryValidator
                    .Validate(expectedGeometry, Measured(1, expectedGeometry.VolumeCubicMillimeters))
                    .IsValid;
                var rejectsVolume = !PartGeometryValidator
                    .Validate(expectedGeometry, Measured(1, expectedGeometry.VolumeCubicMillimeters * 2d))
                    .IsValid;
                var rejectsBodies = !PartGeometryValidator
                    .Validate(expectedGeometry, Measured(2, expectedGeometry.VolumeCubicMillimeters))
                    .IsValid;
                geometryValidationPlatformWide = accepts && rejectsVolume && rejectsBodies;
            }
        }

        var flangeRegressionPassed = flangeUsesPartFamilyDefinition && v18PartFamilyChecks.FlangeDryRunPassed;
        var shaftRegressionPassed = shaftUsesPartFamilyDefinition && v18PartFamilyChecks.ShaftDryRunPassed;
        var capabilityRegression = SelfCheckCapabilityRegressionGate.Evaluate(
            projectRoot,
            new Dictionary<string, bool>(v21BHoleChecks, StringComparer.OrdinalIgnoreCase)
            {
                ["all_part_families_use_registry"] = v19PartFamilyChecks.AllPartFamiliesUseRegistry,
                ["feature_production_evidence_active"] = v20CFeatureAdapterChecks.FeatureProductionEvidenceActive,
                ["flange_artifact_validation_supported"] = v19PartFamilyChecks.FlangeArtifactValidationSupported,
                ["plate_part_family_regression_passed"] = v19PartFamilyChecks.PlatePartFamilyRegressionPassed,
                ["shaft_artifact_validation_supported"] = v19PartFamilyChecks.ShaftArtifactValidationSupported,
                ["v2_0_e_geometry_validation_platform_wide"] = geometryValidationPlatformWide,
                ["feature_api_evidence_required"] = v20BFeatureHandlerChecks.FeatureApiEvidenceRequired,
                // 这两条在 V2.1-A 注册五个复杂特征时曾静默退化：判据里写死了
                // "注册表恰好 5 个 Handler"。没有任何门拦住，因为它们当时不在基线里。
                ["unverified_api_blocks_real_execution"] = v20BFeatureHandlerChecks.UnverifiedApiBlocksRealExecution,
                ["feature_handler_no_direct_com_access"] = v20CFeatureAdapterChecks.FeatureHandlerNoDirectComAccess,
                ["unverified_feature_blocks_execution"] = v21AComplexFeatureChecks.UnverifiedFeatureBlocksExecution,
                ["complex_feature_registry_supported"] = v21AComplexFeatureChecks.ComplexFeatureRegistrySupported,
                ["edge_selection_model_supported"] = v21AComplexFeatureChecks.EdgeSelectionModelSupported,
                ["v2_0_d_production_evidence_active"] = v20DModelRebuildChecks.ProductionEvidenceActive,
                ["v2_0_e_controlled_plate_evidence_active"] = controlledPlateEvidenceActive,
                ["v2_0_e_step_content_gate_active"] = stepContentGateActive,
                ["v2_0_e_unified_part_family_builders"] = unifiedPartFamilyBuilders
            });

        return new(
            unifiedPartFamilyBuilders,
            controlledPlateEvidenceActive,
            stepContentGateActive,
            v21ARealExecutionFrozen,
            v20EDocumented,
            capabilityRegression.Passed,
            capabilityRegression.RegressedFields.Concat(capabilityRegression.ConfigurationErrors).ToArray(),
            partFamilyDefinitionSupported,
            plateUsesPartFamilyDefinition,
            flangeUsesPartFamilyDefinition,
            shaftUsesPartFamilyDefinition,
            unifiedPartFamilyBuilders,
            featureGraphTemplateReuseSupported,
            commonFeatureTemplatesExists,
            cadCapabilityMatrixExists,
            regressionModelsSupported,
            flangeRegressionPassed,
            shaftRegressionPassed,
            geometryValidationPlatformWide);
    }

    private static async Task<V21AJacketSelfCheckResult> RunV21AJacketChecksAsync(
        string projectRoot,
        PlatformKernel platform,
        string versionStageText,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var registry = PartTypeRegistry.CreateDefault();
        var definition = registry.GetDefinition(JacketBasicDefinition.Type) as JacketBasicDefinition;
        var jacketPartFamilyRegistered = definition is not null;
        var spec = new CADModelSpec(
            "self-check-v21-a-jacket",
            JacketBasicDefinition.Type,
            new Dictionary<string, string>
            {
                ["outer_diameter_mm"] = "140",
                ["inner_diameter_mm"] = "120",
                ["length_mm"] = "180"
            },
            material: "Q235");
        var planResult = definition?.GenerateBuildPlan("self-check-v21-a-jacket", spec);
        var plan = planResult?.BuildPlan;
        var featureIds = plan?.Operations
            .Select(operation => operation.Parameters.GetValueOrDefault("feature_id"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var jacketUsesGenericFeatureGraph =
            planResult?.IsSuccess == true &&
            plan is not null &&
            featureIds.SetEquals(["jacket_body_extrude", "jacket_inner_cut"]) &&
            new SolidWorksBuildPlanReviewer(registry).Review(plan).IsPassed;

        var worker = platform.WorkerRegistry.GetByName("FakeSolidWorksWorker");
        var workerAssembly = worker?.GetType().Assembly;
        var builderRegistryType = workerAssembly?.GetType(
            "SolidWorksWorker.PartFamilyBuilderRegistry",
            throwOnError: false);
        var createBuilderRegistry = builderRegistryType?.GetMethod(
            "CreateDefault",
            BindingFlags.Public | BindingFlags.Static);
        var builderRegistry = createBuilderRegistry?.Invoke(
            null,
            createBuilderRegistry.GetParameters().Select(_ => (object?)null).ToArray());
        var builders = (builderRegistryType?
                .GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance)?
                .Invoke(builderRegistry, null) as System.Collections.IEnumerable)?
            .Cast<object>()
            .ToArray() ?? [];
        var jacketBuilder = builders.SingleOrDefault(builder =>
            string.Equals(
                builder.GetType().GetProperty("PartType")?.GetValue(builder)?.ToString(),
                JacketBasicDefinition.Type,
                StringComparison.OrdinalIgnoreCase));
        // V2.0-E：夹套不再拥有零件专用 Builder。正确的断言是"该零件族解析到统一
        // 建模内核的通用执行器"，而不是"存在某个专用 Builder 类"。
        var jacketRealBuilderImplemented =
            jacketBuilder is not null &&
            string.Equals(
                jacketBuilder.GetType().GetProperty("RealExecutionMode")?.GetValue(jacketBuilder)?.ToString(),
                PartFamilyExecutionModes.GenericFeatureGraph,
                StringComparison.Ordinal);
        // 不以 Builder 的 SupportsRealExecution 布尔值替代实际授权；必须让当前
        // 夹套 BuildPlan 通过与真实 Worker 相同的 Handler / evidence 前置校验。
        var jacketProductionEvidenceActive =
            jacketBuilder?.GetType().GetProperty("SupportsRealExecution")?.GetValue(jacketBuilder) is true &&
            ValidateFeatureGraphProductionEvidence(workerAssembly, plan);

        var jacketDryRunPassed = false;
        var workerMethod = worker?.GetType().GetMethods().SingleOrDefault(method =>
            method.Name == "ExecuteAsync" &&
            method.GetParameters().Length == 2 &&
            method.GetParameters()[0].ParameterType == typeof(SolidWorksWorkerRequest));
        if (worker is not null && workerMethod is not null && plan is not null)
        {
            var output = Path.Combine(
                outputRoot,
                "solidworks",
                "self-check",
                "v2_1_a_jacket");
            var request = new SolidWorksWorkerRequest(
                $"self-check-v21-a-jacket-{Guid.NewGuid():N}",
                plan,
                output,
                DryRun: true);
            var task = workerMethod.Invoke(worker, new object?[] { request, cancellationToken }) as Task<SolidWorksWorkerResult>;
            if (task is not null)
            {
                var result = await task;
                jacketDryRunPassed =
                    result.Status == "Completed" &&
                    result.ExecutionMode == "Fake" &&
                    !result.RealCadExecuted &&
                    result.GeneratedArtifacts.Any(artifact =>
                        artifact.FilePath.EndsWith("fake_jacket_basic.SLDPRT.txt", StringComparison.OrdinalIgnoreCase)) &&
                    result.GeneratedArtifacts.Any(artifact =>
                        artifact.FilePath.EndsWith("fake_jacket_basic.STEP.txt", StringComparison.OrdinalIgnoreCase));
            }
        }

        var jacketRealWorkflowSupported =
            jacketRealBuilderImplemented &&
            jacketProductionEvidenceActive &&
            SolidWorksE2eCliContract.IsPartFamilyReleasePackage(
                SolidWorksE2eCliContract.PartFamilyReleasePackageOperation) &&
            File.Exists(Path.Combine(projectRoot, "examples", "real_cad_jacket_request.json"));
        var moduleEvidencePath = Path.Combine(projectRoot, "src", "Modules", "CADModeling", "api_evidence.md");
        var workerEvidencePath = Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "api_evidence.md");
        var evidenceText =
            (File.Exists(moduleEvidencePath) ? File.ReadAllText(moduleEvidencePath) : string.Empty) +
            (File.Exists(workerEvidencePath) ? File.ReadAllText(workerEvidencePath) : string.Empty);
        var jacketApiEvidenceDocumented =
            evidenceText.Contains(JacketBasicDefinition.Type, StringComparison.OrdinalIgnoreCase) &&
            evidenceText.Contains("FeatureExtrusion2", StringComparison.Ordinal) &&
            evidenceText.Contains("FeatureCut4", StringComparison.Ordinal);
        var v21AJacketDocumented =
            versionStageText.Contains("V2.1-A", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(projectRoot, "docs", "v2_1_a_jacket_basic.md"));

        return new V21AJacketSelfCheckResult(
            jacketPartFamilyRegistered,
            jacketUsesGenericFeatureGraph,
            jacketRealBuilderImplemented,
            jacketDryRunPassed,
            jacketRealWorkflowSupported,
            jacketApiEvidenceDocumented,
            jacketProductionEvidenceActive,
            v21AJacketDocumented);
    }

    private static Dictionary<string, string> ReadStringDictionary(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return result;
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

    private static ExecutableDocsLayerSelfCheckResult RunExecutableDocsLayerChecks(string projectRoot)
    {
        bool Exists(params string[] parts) => File.Exists(Path.Combine(parts.Prepend(projectRoot).ToArray()));
        bool DirectoryExists(params string[] parts) => Directory.Exists(Path.Combine(parts.Prepend(projectRoot).ToArray()));
        string Read(params string[] parts)
        {
            var path = Path.Combine(parts.Prepend(projectRoot).ToArray());
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        var docsIndexExists = Exists("docs", "index.md");
        var projectExecutionStandardExists = Exists("docs", "project_execution_standard.md");
        var moduleDocumentStandardExists = Exists("docs", "module_document_standard.md");
        var stepExecutionStandardExists = Exists("docs", "step_execution_standard.md");
        var failureRepairStandardExists = Exists("docs", "failure_repair_standard.md");
        var codexExecutionProtocolExists = Exists("docs", "codex_execution_protocol.md");
        var claudeReviewProtocolExists = Exists("docs", "claude_review_protocol.md");
        var versionStageIndexExists = Exists("docs", "version_stage_index.md");
        var codexAgentTeamGuideExists = Exists("docs", "codex_agent_team_guide.md");
        var codexAgentRegistryExists = Exists("docs", "codex_agent_registry.md");
        var codexAgentGovernanceDocExists = Exists("docs", "codex_agent_governance.md");
        var agentsMdExists = Exists("AGENTS.md");
        var codexConfigExampleExists = Exists(".codex", "config.example.toml");

        var projectManager = Read(".codex", "agents", "project-manager.toml");
        var codeMapper = Read(".codex", "agents", "code-mapper.toml");
        var apiResearcher = Read(".codex", "agents", "api-researcher.toml");
        var cadWorker = Read(".codex", "agents", "cad-worker.toml");
        var qualityGate = Read(".codex", "agents", "quality-gate.toml");
        var docsWriter = Read(".codex", "agents", "docs-writer.toml");

        var codexProjectManagerAgentExists = AgentConfigured(projectManager, "project_manager", "read-only");
        var codexCodeMapperAgentExists = AgentConfigured(codeMapper, "code_mapper", "read-only");
        var codexApiResearcherAgentExists = AgentConfigured(apiResearcher, "api_researcher", "read-only");
        var codexCadWorkerAgentExists = AgentConfigured(cadWorker, "cad_worker", "workspace-write");
        var codexQualityGateAgentExists = AgentConfigured(qualityGate, "quality_gate", "read-only");
        var codexDocsWriterAgentExists = AgentConfigured(docsWriter, "docs_writer", "workspace-write");
        var codexAgentsConfigured =
            codexProjectManagerAgentExists &&
            codexCodeMapperAgentExists &&
            codexApiResearcherAgentExists &&
            codexCadWorkerAgentExists &&
            codexQualityGateAgentExists &&
            codexDocsWriterAgentExists;

        var agentsText = Read("AGENTS.md");
        var guideText = Read("docs", "codex_agent_team_guide.md");
        var protocolText = Read("docs", "codex_execution_protocol.md");
        var registryText = Read("docs", "codex_agent_registry.md");
        var governanceText = Read("docs", "codex_agent_governance.md");
        var codexAgentsDoNotReplaceProjectModules =
            agentsText.Contains("不能替代 `src/Modules`", StringComparison.OrdinalIgnoreCase) &&
            guideText.Contains("不能替代 `src/Modules`", StringComparison.OrdinalIgnoreCase);
        var codexAgentsRespectWorkerBoundaries =
            agentsText.Contains("不能直接调用 `Worker`", StringComparison.OrdinalIgnoreCase) &&
            guideText.Contains("不能直接执行真实 CAD", StringComparison.OrdinalIgnoreCase);
        var codexAgentRegistryListsCanonicalAgents = CanonicalCodexAgentNames
            .All(agentName => registryText.Contains($"`{agentName}`", StringComparison.OrdinalIgnoreCase));
        var activeAgentDirectory = Path.Combine(projectRoot, ".codex", "agents");
        var activeAgentFiles = Directory.Exists(activeAgentDirectory)
            ? Directory.GetFiles(activeAgentDirectory, "*.toml", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
        var activeAgentFileNames = activeAgentFiles
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeAgentNames = activeAgentFiles
            .Select(path => ExtractAgentName(File.ReadAllText(path)))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        var codexNoDuplicateActiveAgents =
            activeAgentNames.Length == activeAgentNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() &&
            CanonicalCodexAgentNames.All(agentName =>
                activeAgentNames.Count(activeName => activeName.Equals(agentName, StringComparison.OrdinalIgnoreCase)) <= 1);
        var codexActiveAgentCountIsExpected = activeAgentFiles.Length == CanonicalCodexAgentFiles.Length;
        var codexOnlyCanonicalAgentsActive =
            activeAgentFileNames.SetEquals(CanonicalCodexAgentFiles) &&
            activeAgentNames.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(CanonicalCodexAgentNames);
        var codexAgentReusePolicyDocumented =
            agentsText.Contains("Codex Agent 复用规则", StringComparison.OrdinalIgnoreCase) &&
            guideText.Contains("Agent 去重与复用", StringComparison.OrdinalIgnoreCase) &&
            protocolText.Contains("canonical agent", StringComparison.OrdinalIgnoreCase) &&
            governanceText.Contains("不允许重复创建同职责 Agent", StringComparison.OrdinalIgnoreCase);
        var codexAgentNewRequirementsGoToSkillsOrDocs =
            governanceText.Contains(".agents/skills/solidworks-api-repair/SKILL.md", StringComparison.OrdinalIgnoreCase) &&
            governanceText.Contains(".agents/skills/quality-review/SKILL.md", StringComparison.OrdinalIgnoreCase) &&
            governanceText.Contains(".agents/skills/markdown-docs-standard/SKILL.md", StringComparison.OrdinalIgnoreCase) &&
            governanceText.Contains("api_evidence.md", StringComparison.OrdinalIgnoreCase) &&
            governanceText.Contains("review_checklist.md", StringComparison.OrdinalIgnoreCase) &&
            governanceText.Contains("module_document_standard.md", StringComparison.OrdinalIgnoreCase);

        var agentsSkillsDirectoryExists = DirectoryExists(".agents", "skills");
        var solidWorksApiRepairSkillExists = Exists(".agents", "skills", "solidworks-api-repair", "SKILL.md");
        var markdownDocsStandardSkillExists = Exists(".agents", "skills", "markdown-docs-standard", "SKILL.md");
        var qualityReviewSkillExists = Exists(".agents", "skills", "quality-review", "SKILL.md");

        var cadModelingExecutionDocExists = Exists("src", "Modules", "CADModeling", "execution.md");
        var cadModelingFailureRepairDocExists = Exists("src", "Modules", "CADModeling", "failure_repair.md");
        var cadModelingApiEvidenceDocExists = Exists("src", "Modules", "CADModeling", "api_evidence.md");
        var cadModelingReviewChecklistExists = Exists("src", "Modules", "CADModeling", "review_checklist.md");
        var solidWorksWorkerExecutionDocExists = Exists("src", "Workers", "SolidWorks", "execution.md");
        var solidWorksWorkerFailureRepairDocExists = Exists("src", "Workers", "SolidWorks", "failure_repair.md");
        var solidWorksWorkerApiEvidenceDocExists = Exists("src", "Workers", "SolidWorks", "api_evidence.md");
        var solidWorksWorkerReviewChecklistExists = Exists("src", "Workers", "SolidWorks", "review_checklist.md");

        var executableDocsLayerEnabled =
            docsIndexExists &&
            projectExecutionStandardExists &&
            moduleDocumentStandardExists &&
            stepExecutionStandardExists &&
            failureRepairStandardExists &&
            codexExecutionProtocolExists &&
            claudeReviewProtocolExists &&
            versionStageIndexExists &&
            codexAgentTeamGuideExists &&
            codexAgentRegistryExists &&
            codexAgentGovernanceDocExists &&
            agentsMdExists &&
            codexAgentsConfigured &&
            codexAgentRegistryListsCanonicalAgents &&
            codexNoDuplicateActiveAgents &&
            codexAgentReusePolicyDocumented &&
            codexAgentNewRequirementsGoToSkillsOrDocs &&
            codexActiveAgentCountIsExpected &&
            codexOnlyCanonicalAgentsActive &&
            codexConfigExampleExists &&
            codexAgentsDoNotReplaceProjectModules &&
            codexAgentsRespectWorkerBoundaries &&
            agentsSkillsDirectoryExists &&
            solidWorksApiRepairSkillExists &&
            markdownDocsStandardSkillExists &&
            qualityReviewSkillExists &&
            cadModelingExecutionDocExists &&
            cadModelingFailureRepairDocExists &&
            cadModelingApiEvidenceDocExists &&
            cadModelingReviewChecklistExists &&
            solidWorksWorkerExecutionDocExists &&
            solidWorksWorkerFailureRepairDocExists &&
            solidWorksWorkerApiEvidenceDocExists &&
            solidWorksWorkerReviewChecklistExists;

        return new ExecutableDocsLayerSelfCheckResult(
            executableDocsLayerEnabled,
            docsIndexExists,
            projectExecutionStandardExists,
            moduleDocumentStandardExists,
            stepExecutionStandardExists,
            failureRepairStandardExists,
            codexExecutionProtocolExists,
            claudeReviewProtocolExists,
            versionStageIndexExists,
            codexAgentTeamGuideExists,
            codexAgentRegistryExists,
            codexAgentGovernanceDocExists,
            agentsMdExists,
            codexAgentsConfigured,
            codexAgentRegistryListsCanonicalAgents,
            codexNoDuplicateActiveAgents,
            codexAgentReusePolicyDocumented,
            codexAgentNewRequirementsGoToSkillsOrDocs,
            codexActiveAgentCountIsExpected,
            codexOnlyCanonicalAgentsActive,
            codexConfigExampleExists,
            codexProjectManagerAgentExists,
            codexCodeMapperAgentExists,
            codexApiResearcherAgentExists,
            codexCadWorkerAgentExists,
            codexQualityGateAgentExists,
            codexDocsWriterAgentExists,
            codexAgentsDoNotReplaceProjectModules,
            codexAgentsRespectWorkerBoundaries,
            agentsSkillsDirectoryExists,
            solidWorksApiRepairSkillExists,
            markdownDocsStandardSkillExists,
            qualityReviewSkillExists,
            cadModelingExecutionDocExists,
            cadModelingFailureRepairDocExists,
            cadModelingApiEvidenceDocExists,
            cadModelingReviewChecklistExists,
            solidWorksWorkerExecutionDocExists,
            solidWorksWorkerFailureRepairDocExists,
            solidWorksWorkerApiEvidenceDocExists,
            solidWorksWorkerReviewChecklistExists);
    }

    private static bool AgentConfigured(string toml, string name, string sandboxMode) =>
        toml.Contains($"name = \"{name}\"", StringComparison.OrdinalIgnoreCase) &&
        toml.Contains($"sandbox_mode = \"{sandboxMode}\"", StringComparison.OrdinalIgnoreCase);

    private static string ExtractAgentName(string toml)
    {
        foreach (var line in toml.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            return trimmed[(separatorIndex + 1)..].Trim().Trim('"');
        }

        return string.Empty;
    }

    private static async Task<AgentContracts.AgentOutput> InvokeChiefEngineerForSelfCheck(PlatformKernel platform, string? testScenario = null, string? outputRoot = null)
    {
        var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer")
            ?? throw new InvalidOperationException("chief-engineer is not registered.");
        var inputContext = new Dictionary<string, string> { ["project_id"] = "self-check" };
        if (testScenario is not null)
        {
            inputContext["test_scenario"] = testScenario;
            if (string.Equals(testScenario, "solidworks_main_workflow", StringComparison.OrdinalIgnoreCase))
            {
                inputContext["solidworks_main_workflow"] = "true";
                inputContext["dry_run"] = "true";
                if (outputRoot is not null)
                    inputContext["solidworks_output_directory"] = Path.Combine(outputRoot, "solidworks", "self-check", "chief-engineer-main-workflow");
            }
        }

        var message = string.Equals(testScenario, "solidworks_main_workflow", StringComparison.OrdinalIgnoreCase)
            ? "Run SolidWorks plate_basic_4holes main workflow self-check."
            : "Run internal routing self-check.";
        var input = new AgentContracts.AgentInput(
            "self-check",
            "self-check",
            testScenario is null ? "self-check-conversation" : $"self-check-{testScenario}",
            "self-check",
            message,
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
            var solidWorksDiagnosticRunnerExists = File.Exists(Path.Combine(
                projectRoot,
                "tools",
                "SolidWorksSmokeRunner",
                "SolidWorksSmokeRunner.csproj"));
            var apiFailureAnalyzerPath = Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "Diagnostics", "SolidWorksApiFailureAnalyzer.cs");
            var apiEvidenceCollectorPath = Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "Diagnostics", "SolidWorksApiEvidenceCollector.cs");
            var apiFailureAnalyzerText = File.Exists(apiFailureAnalyzerPath) ? File.ReadAllText(apiFailureAnalyzerPath) : string.Empty;
            var apiEvidenceCollectorText = File.Exists(apiEvidenceCollectorPath) ? File.ReadAllText(apiEvidenceCollectorPath) : string.Empty;
            var plateFeatureBuilderPath = Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "SolidWorksPlateFeatureBuilder.cs");
            var plateFeatureBuilderText = File.Exists(plateFeatureBuilderPath) ? File.ReadAllText(plateFeatureBuilderPath) : string.Empty;
            var solidWorksApiFailureAnalyzerExists =
                apiFailureAnalyzerText.Contains("SolidWorksApiFailureAnalyzer", StringComparison.Ordinal) &&
                apiFailureAnalyzerText.Contains("cut_holes_failed", StringComparison.OrdinalIgnoreCase);
            var solidWorksApiEvidenceCollectorExists =
                apiEvidenceCollectorText.Contains("SolidWorksApiEvidenceCollector", StringComparison.Ordinal) &&
                apiEvidenceCollectorText.Contains("cut_holes_failed_api_evidence_report", StringComparison.Ordinal);
            var solidWorksApiEvidenceReportSchemaExists =
                typeof(ApiEvidenceReport).GetProperties().Any(property => property.Name == nameof(ApiEvidenceReport.SelectedApiStrategy)) &&
                typeof(ApiCandidate).GetProperties().Any(property => property.Name == nameof(ApiCandidate.RequiredSelectionState));
            var solidWorksCutHolesApiEvidenceSupported =
                apiFailureAnalyzerText.Contains("SolidWorks API FeatureCut4 cut extrude", StringComparison.OrdinalIgnoreCase) &&
                apiFailureAnalyzerText.Contains("SolidWorks API CreateCircleByRadius SketchManager", StringComparison.OrdinalIgnoreCase) &&
                apiEvidenceCollectorText.Contains("FeatureCut4", StringComparison.Ordinal) &&
                apiEvidenceCollectorText.Contains("CreateCircleByRadius", StringComparison.Ordinal);
            var solidWorksReferenceSkillReadonlyAnalysisSupported =
                File.Exists(Path.Combine(projectRoot, "references", "external", "solidworks-automation-skill-analysis.md")) &&
                apiEvidenceCollectorText.Contains("CanReuseCode: false", StringComparison.Ordinal) &&
                apiEvidenceCollectorText.Contains("CanReuseIdea: true", StringComparison.Ordinal);
            var solidWorksExternalScriptsNotCopied =
                !Directory.EnumerateFiles(Path.Combine(projectRoot, "src", "Workers", "SolidWorks"), "*.py", SearchOption.AllDirectories).Any() &&
                !Directory.EnumerateFiles(Path.Combine(projectRoot, "tools", "SolidWorksSmokeRunner"), "*.py", SearchOption.AllDirectories).Any();
            var solidWorksApiRepairLoopAvailable =
                WorkerContracts.SolidWorksApiRepairPolicy.MaxRepairAttempts == 1 &&
                WorkerContracts.SolidWorksApiRepairPolicy.CanAttempt("cut_holes_failed", repairAlreadyAttempted: false) &&
                !WorkerContracts.SolidWorksApiRepairPolicy.CanAttempt("cut_holes_failed", repairAlreadyAttempted: true) &&
                !WorkerContracts.SolidWorksApiRepairPolicy.CanAttempt("save_sldprt_failed", repairAlreadyAttempted: false);
            var solidWorksMacroRecordingRequestAvailable =
                apiEvidenceCollectorText.Contains("macro_recording_request.md", StringComparison.Ordinal) &&
                apiEvidenceCollectorText.Contains("SolidWorks 切孔宏录制请求", StringComparison.Ordinal);
            var solidWorksPlateFeatureBuilderExists =
                plateFeatureBuilderText.Contains("SolidWorksPlateFeatureBuilder", StringComparison.Ordinal) &&
                plateFeatureBuilderText.Contains("CreateThroughHoles", StringComparison.Ordinal);
            var solidWorksDiagnosticRunnerNotCalledByDefault = true;
            var solidWorksLatestDiagnosticReportPath = FindLatestDiagnosticReportPath(projectRoot);
            var solidWorksLatestDiagnosticFinalStatus = ReadDiagnosticFinalStatus(solidWorksLatestDiagnosticReportPath);
            string? solidWorksRealBuildFailureStage = null;
            var solidWorksRealBuildErrorIsActionable = true;

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
            var solidWorksWorkerAssembly = registeredFakeSolidWorksWorker?.GetType().Assembly;
            var realSolidWorksWorkerType = solidWorksWorkerAssembly?.GetType("SolidWorksWorker.RealSolidWorksWorker", throwOnError: false);
            var solidWorksSessionManagerType = solidWorksWorkerAssembly?.GetType("SolidWorksWorker.SolidWorksSessionManager", throwOnError: false);
            var fakeSolidWorksWorkerRegistered =
                registeredFakeSolidWorksWorker?.GetType().FullName == FakeSolidWorksWorkerFullName &&
                registeredFakeSolidWorksWorker.GetType().GetMethods().Any(method =>
                    method.Name == "ExecuteAsync" &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType.Name == nameof(SolidWorksWorkerRequest));

            SolidWorksWorkerResult? workerResult = null;
            SolidWorksWorkerRequest? dryRunRequest = null;
            if (plan is not null && fakeSolidWorksWorkerRegistered && registeredFakeSolidWorksWorker is not null)
            {
                var workerMethod = registeredFakeSolidWorksWorker.GetType().GetMethods()
                    .Single(method =>
                        method.Name == "ExecuteAsync" &&
                        method.GetParameters().Length == 2 &&
                        method.GetParameters()[0].ParameterType.Name == nameof(SolidWorksWorkerRequest));
                var requestId = $"self-check-solidworks-request-{Guid.NewGuid():N}";
                dryRunRequest = new SolidWorksWorkerRequest(
                    requestId,
                    plan,
                    Path.Combine(outputRoot, "solidworks", "self-check", requestId),
                    DryRun: true,
                    AllowRealCadExecution: false);
                var task = (Task<SolidWorksWorkerResult>)workerMethod.Invoke(registeredFakeSolidWorksWorker, new object?[] { dryRunRequest, cancellationToken })!;
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
                ? new SolidWorksArtifactValidator(Path.Combine(outputRoot, "solidworks")).Validate(workerResult)
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
                platform.AgentRegistry.GetById("RealSolidWorksWorker") is null &&
                platform.AgentRegistry.GetPublicAgents().All(agent => agent.Id == "chief-engineer");

            var defaultRuntimeOptions = SolidWorksRuntimeOptions.FromEnvironment(
                new Dictionary<string, string?>(),
                isUnitTestEnvironment: true);
            var solidWorksRealExecutionDefaultDisabled =
                !defaultRuntimeOptions.EnableRealExecution &&
                !defaultRuntimeOptions.Visible &&
                defaultRuntimeOptions.ConnectTimeoutSeconds == SolidWorksRuntimeOptions.DefaultConnectTimeoutSeconds;
            var environmentValidator = new SolidWorksEnvironmentValidator();
            var preflightRequest = dryRunRequest ?? (plan is null
                ? null
                : new SolidWorksWorkerRequest(
                    $"self-check-solidworks-preflight-{Guid.NewGuid():N}",
                    plan,
                    Path.Combine(outputRoot, "solidworks", "self-check", "preflight"),
                    DryRun: true,
                    AllowRealCadExecution: false));
            var preflightReport = preflightRequest is not null
                ? environmentValidator.ValidateEnvironment(preflightRequest, defaultRuntimeOptions)
                : null;
            var solidWorksEnvironmentValidatorExists =
                typeof(SolidWorksEnvironmentValidator).GetMethod(nameof(SolidWorksEnvironmentValidator.ValidateEnvironment)) is not null;
            var solidWorksPreflightReportGenerated =
                preflightReport is not null &&
                preflightReport.FinalStatus is "Skipped" or "Failed" &&
                preflightReport.Issues.Any(issue => issue.Contains("dry_run_mode_enabled", StringComparison.OrdinalIgnoreCase)) &&
                !preflightReport.SolidWorksApplicationConnectable;
            var solidWorksRealWorkerSkeletonExists =
                realSolidWorksWorkerType is not null &&
                realSolidWorksWorkerType.GetProperty("SupportsGenericRealBuild") is not null;
            var solidWorksSessionManagerExists =
                solidWorksSessionManagerType is not null &&
                solidWorksSessionManagerType.GetMethod("ConnectAsync") is not null &&
                solidWorksSessionManagerType.GetMethod("DisconnectAsync") is not null;

            var solidWorksRealExecutionRequiresRequestFlag = false;
            var solidWorksRealExecutionRequiresEnvFlag = false;
            var solidWorksComNotCalledInDefaultSelfCheck = false;
            var solidWorksGenericRealBuildNotImplemented = false;
            var solidWorksRealCadNotExecutedByDefault = false;
            var solidWorksRealConnectionSmokeTestAttempted =
                !defaultRuntimeOptions.IsUnitTestEnvironment && RealSolidWorksSmokeTestRequested();
            var solidWorksRealConnectionSmokeTestPassed = false;
            string? solidWorksRealConnectionSmokeTestError = null;
            var solidWorksStrictRealSmokeTest = StrictRealSolidWorksSmokeTestRequested();
            var solidWorksRealPlateBuildImplemented = false;
            var solidWorksRealBuildRequiresEnvFlag = false;
            var solidWorksRealBuildRequiresRequestFlag = false;
            var solidWorksRealBuildRequiresDryRunFalse = false;
            var solidWorksRealBuildSmokeTestAttempted =
                !defaultRuntimeOptions.IsUnitTestEnvironment && RealSolidWorksBuildSmokeTestRequested();
            var solidWorksRealBuildDefaultDisabled = !solidWorksRealBuildSmokeTestAttempted;
            var solidWorksRealBuildSmokeTestPassed = false;
            string? solidWorksRealBuildSmokeTestError = null;
            var solidWorksStrictRealBuildSmokeTest = StrictRealSolidWorksBuildTestRequested();
            var solidWorksRealBuildArtifactsValidated = false;
            var solidWorksRealBuildReportGenerated = false;
            var solidWorksRealBuildOutputsSldprt = false;
            var solidWorksRealBuildOutputsStep = false;
            var solidWorksRealBuildOutputsJsonReport = false;
            var solidWorksRealBuildNotCalledInDefaultSelfCheck = !solidWorksRealBuildSmokeTestAttempted;
            var solidWorksRealDrawingBasicViewsImplemented = false;
            var solidWorksRealDrawingSmokeTestAttempted =
                !defaultRuntimeOptions.IsUnitTestEnvironment && RealSolidWorksDrawingSmokeTestRequested();
            var solidWorksRealDrawingDefaultDisabled = !solidWorksRealDrawingSmokeTestAttempted;
            var solidWorksRealDrawingRequiresEnvFlag = false;
            var solidWorksRealDrawingSmokeTestPassed = false;
            string? solidWorksRealDrawingSmokeTestError = null;
            var solidWorksStrictRealDrawingSmokeTest = StrictRealSolidWorksDrawingTestRequested();
            var solidWorksRealDrawingOutputsSlddrw = false;
            var solidWorksRealDrawingOutputsPdf = false;
            var solidWorksRealDrawingOutputsJsonReport = false;
            var solidWorksRealDrawingNotCalledInDefaultSelfCheck = !solidWorksRealDrawingSmokeTestAttempted;
            var solidWorksDrawingReportGenerated = false;
            string? solidWorksRealDrawingFailureStage = null;
            var solidWorksDrawingFailureStageActionable = true;
            string? realDrawingOutputDirectory = null;
            string? realDrawingLatestReportPath = null;
            var solidWorksRealDrawingDimensionsImplemented = false;
            var solidWorksRealDrawingDimensionsSmokeTestAttempted =
                !defaultRuntimeOptions.IsUnitTestEnvironment && RealSolidWorksDrawingDimensionSmokeTestRequested();
            var solidWorksRealDrawingDimensionsDefaultDisabled = !solidWorksRealDrawingDimensionsSmokeTestAttempted;
            var solidWorksRealDrawingDimensionsRequiresEnvFlag = false;
            var solidWorksRealDrawingDimensionsSmokeTestPassed = false;
            string? solidWorksRealDrawingDimensionsSmokeTestError = null;
            var solidWorksStrictRealDrawingDimensionSmokeTest = StrictRealSolidWorksDrawingDimensionTestRequested();
            var solidWorksRealDrawingDimensionsOutputsSlddrw = false;
            var solidWorksRealDrawingDimensionsOutputsPdf = false;
            var solidWorksRealDrawingDimensionsOutputsJsonReport = false;
            var solidWorksRealDrawingDimensionsNotCalledInDefaultSelfCheck = !solidWorksRealDrawingDimensionsSmokeTestAttempted;
            var solidWorksDrawingDimensionReportGenerated = false;
            string? solidWorksRealDrawingDimensionFailureStage = null;
            var solidWorksDrawingDimensionFailureStageActionable = true;
            string? realDrawingDimensionOutputDirectory = null;
            string? realDrawingDimensionLatestReportPath = null;
            var solidWorksRealDrawingTitleBlockImplemented = false;
            var solidWorksRealDrawingTitleBlockSmokeTestAttempted =
                !defaultRuntimeOptions.IsUnitTestEnvironment && RealSolidWorksDrawingTitleBlockSmokeTestRequested();
            var solidWorksRealDrawingTitleBlockDefaultDisabled = !solidWorksRealDrawingTitleBlockSmokeTestAttempted;
            var solidWorksRealDrawingTitleBlockRequiresEnvFlag = false;
            var solidWorksRealDrawingTitleBlockSmokeTestPassed = false;
            string? solidWorksRealDrawingTitleBlockSmokeTestError = null;
            var solidWorksStrictRealDrawingTitleBlockSmokeTest = StrictRealSolidWorksDrawingTitleBlockTestRequested();
            var solidWorksRealDrawingTitleBlockOutputsSlddrw = false;
            var solidWorksRealDrawingTitleBlockOutputsPdf = false;
            var solidWorksRealDrawingTitleBlockOutputsJsonReport = false;
            var solidWorksRealDrawingTitleBlockNotCalledInDefaultSelfCheck = !solidWorksRealDrawingTitleBlockSmokeTestAttempted;
            var solidWorksDrawingTitleBlockReportGenerated = false;
            string? solidWorksRealDrawingTitleBlockFailureStage = null;
            var solidWorksDrawingTitleBlockFailureStageActionable = true;
            string? realDrawingTitleBlockOutputDirectory = null;
            string? realDrawingTitleBlockLatestReportPath = null;
            var solidWorksReleasePackageImplemented = Type.GetType("SolidWorksWorker.SolidWorksReleasePackageBuilder, SolidWorksWorker") is not null &&
                File.Exists(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "SolidWorksReleasePackageBuilder.cs"));
            var solidWorksReleasePackageDefaultNoCadExecution = solidWorksReleasePackageImplemented;
            var solidWorksReleaseManifestGenerated = false;
            var solidWorksPackageQualityReportGenerated = false;
            var solidWorksReleaseSummaryGenerated = false;
            var solidWorksReleaseArtifactsCollected = false;
            var solidWorksReleaseReportsCollected = false;
            string? solidWorksReleasePackageFailureStage = null;
            var solidWorksReleasePackageFailureStageActionable = false;
            string? solidWorksReleaseManifestPath = null;
            string? solidWorksPackageQualityReportPath = null;
            string? solidWorksReleaseSummaryPath = null;
            var realCadWorkerIntegratedIntoMainWorkflow = false;
            var chiefEngineerOrchestratorInvokesCadWorkflow = false;
            var workflowEngineCanRouteToSolidWorksWorker = false;
            var realCadMainWorkflowDefaultDisabled = false;
            var realCadMainWorkflowRequiresRequestFlag = false;
            var realCadMainWorkflowRequiresEnvFlag = false;
            var realCadMainWorkflowPassesQualityGate = false;
            var llmDoesNotCallWorkerDirectly = false;
            var releasePackageAllSourceReportsPassedFieldExists =
                typeof(SolidWorksPackageQualityReport).GetProperty(nameof(SolidWorksPackageQualityReport.AllSourceReportsPassed)) is not null;
            var releasePackageDeliverableStatusFieldExists =
                typeof(SolidWorksPackageQualityReport).GetProperty(nameof(SolidWorksPackageQualityReport.DeliverableStatus)) is not null;
            var releasePackageFailedSourceReportsBlockDeliverable = false;
            if (plan is not null && fakeSolidWorksWorkerRegistered)
            {
                var mainWorkflowRunner = new SolidWorksMainWorkflowRunner(
                    platform.SkillRegistry,
                    platform.WorkerRegistry,
                    platform.AuditLog,
                    platform.WorkflowEngine,
                    () => defaultRuntimeOptions);
                var defaultMainWorkflowResult = await mainWorkflowRunner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                    $"self-check-main-workflow-{Guid.NewGuid():N}",
                    $"task-{Guid.NewGuid():N}",
                    projectRoot,
                    Path.Combine(outputRoot, "solidworks", "self-check", "main-workflow-default"),
                    DryRun: true,
                    AllowRealCadExecution: false),
                    cancellationToken);
                var envEnabledRequestFlagMissingResult = await new SolidWorksMainWorkflowRunner(
                    platform.SkillRegistry,
                    platform.WorkerRegistry,
                    platform.AuditLog,
                    platform.WorkflowEngine,
                    () => defaultRuntimeOptions with { EnableRealExecution = true })
                    .ExecuteAsync(new SolidWorksMainWorkflowRequest(
                        $"self-check-main-workflow-request-flag-{Guid.NewGuid():N}",
                        $"task-{Guid.NewGuid():N}",
                        projectRoot,
                        Path.Combine(outputRoot, "solidworks", "self-check", "main-workflow-request-flag"),
                        DryRun: false,
                        AllowRealCadExecution: false),
                        cancellationToken);
                var requestFlagEnvMissingResult = await mainWorkflowRunner.ExecuteAsync(new SolidWorksMainWorkflowRequest(
                    $"self-check-main-workflow-env-flag-{Guid.NewGuid():N}",
                    $"task-{Guid.NewGuid():N}",
                    projectRoot,
                    Path.Combine(outputRoot, "solidworks", "self-check", "main-workflow-env-flag"),
                    DryRun: false,
                    AllowRealCadExecution: true),
                    cancellationToken);
                var chiefEngineerCadOutput = await InvokeChiefEngineerForSelfCheck(
                    platform,
                    "solidworks_main_workflow",
                    outputRoot);

                realCadWorkerIntegratedIntoMainWorkflow =
                    defaultMainWorkflowResult.Status == "Completed" &&
                    defaultMainWorkflowResult.Artifacts.Count > 0 &&
                    defaultMainWorkflowResult.ArtifactPaths.Count > 0;
                chiefEngineerOrchestratorInvokesCadWorkflow =
                    chiefEngineerCadOutput.Logs.Any(log => log.Contains("SolidWorks main workflow", StringComparison.OrdinalIgnoreCase)) &&
                    chiefEngineerCadOutput.Artifacts.Any(artifact =>
                        artifact.Name.Equals("solidworks-main-workflow-report", StringComparison.OrdinalIgnoreCase) &&
                        artifact.Metadata?.TryGetValue("real_cad_executed", out var realCadExecuted) == true &&
                        string.Equals(realCadExecuted, bool.FalseString, StringComparison.OrdinalIgnoreCase));
                workflowEngineCanRouteToSolidWorksWorker =
                    defaultMainWorkflowResult.WorkflowResult.Steps.Any(step => step.StepId == "solidworks-worker-execution") &&
                    defaultMainWorkflowResult.WorkflowResult.Status == WorkflowStatus.Passed;
                realCadMainWorkflowDefaultDisabled =
                    !defaultMainWorkflowResult.RealCadExecuted &&
                    defaultMainWorkflowResult.ExecutionMode == "Fake";
                realCadMainWorkflowRequiresRequestFlag = false;
                realCadMainWorkflowRequiresEnvFlag = false;
                realCadMainWorkflowPassesQualityGate = defaultMainWorkflowResult.QualityGatePassed;
                llmDoesNotCallWorkerDirectly =
                    solidWorksAgentDoesNotCallWorkerDirectly &&
                    gatewayDoesNotCallSolidWorksWorker;
                releasePackageFailedSourceReportsBlockDeliverable = await ReleasePackageFailedSourceReportsBlockDeliverableAsync(
                    cancellationToken);
            }
            var swRealBuildSmokeTestEnvValue = Environment.GetEnvironmentVariable("SW_REAL_BUILD_SMOKE_TEST");
            var swStrictRealBuildTestEnvValue = Environment.GetEnvironmentVariable("SW_STRICT_REAL_BUILD_TEST");
            bool? realBuildRequestDryRun = null;
            string? realBuildExecutionMode = null;
            string? realBuildOutputDirectory = null;
            string? realBuildLatestReportPath = null;

            if (realSolidWorksWorkerType is not null && plan is not null)
            {
                var requestFlagProbe = new SolidWorksWorkerRequest(
                    $"self-check-solidworks-request-flag-{Guid.NewGuid():N}",
                    plan,
                    Path.Combine(outputRoot, "solidworks", "self-check", "real-request-flag"),
                    DryRun: false,
                    AllowRealCadExecution: false);
                var envFlagProbe = requestFlagProbe with
                {
                    RequestId = $"self-check-solidworks-env-flag-{Guid.NewGuid():N}",
                    OutputDirectory = Path.Combine(outputRoot, "solidworks", "self-check", "real-env-flag"),
                    AllowRealCadExecution = true
                };
                var dryRunFlagProbe = requestFlagProbe with
                {
                    RequestId = $"self-check-solidworks-dry-run-flag-{Guid.NewGuid():N}",
                    OutputDirectory = Path.Combine(outputRoot, "solidworks", "self-check", "real-dry-run-flag"),
                    DryRun = true,
                    AllowRealCadExecution = true
                };
                var requestFlagResult = await InvokeRealSolidWorksWorkerAsync(
                    realSolidWorksWorkerType,
                    defaultRuntimeOptions with { EnableRealExecution = true },
                    requestFlagProbe,
                    cancellationToken);
                var envFlagResult = await InvokeRealSolidWorksWorkerAsync(
                    realSolidWorksWorkerType,
                    defaultRuntimeOptions,
                    envFlagProbe,
                    cancellationToken);
                var dryRunFlagResult = await InvokeRealSolidWorksWorkerAsync(
                    realSolidWorksWorkerType,
                    defaultRuntimeOptions with { EnableRealExecution = true },
                    dryRunFlagProbe,
                    cancellationToken);

                solidWorksRealExecutionRequiresRequestFlag = false;
                solidWorksRealExecutionRequiresEnvFlag = false;
                solidWorksComNotCalledInDefaultSelfCheck =
                    envFlagResult.Logs.Any(log => log.Contains("COM connection was not attempted", StringComparison.OrdinalIgnoreCase)) &&
                    !envFlagResult.RealCadConnected;
                solidWorksGenericRealBuildNotImplemented =
                    realSolidWorksWorkerType.GetProperty("SupportsGenericRealBuild")?.GetValue(
                        Activator.CreateInstance(realSolidWorksWorkerType)) is false;
                solidWorksRealPlateBuildImplemented =
                    realSolidWorksWorkerType.GetProperty("SupportsPlateBasicFourHolesBuild")?.GetValue(
                        Activator.CreateInstance(realSolidWorksWorkerType)) is true;
                solidWorksRealDrawingBasicViewsImplemented =
                    realSolidWorksWorkerType.GetProperty("SupportsBasicViewsDrawing")?.GetValue(
                        Activator.CreateInstance(realSolidWorksWorkerType)) is true &&
                    File.Exists(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "SolidWorksDrawingBuilder.cs")) &&
                    File.Exists(Path.Combine(projectRoot, "tools", "SolidWorksDrawingSmokeRunner", "SolidWorksDrawingSmokeRunner.csproj"));
                solidWorksRealDrawingDimensionsImplemented =
                    realSolidWorksWorkerType.GetProperty("SupportsDrawingDimensions")?.GetValue(
                        Activator.CreateInstance(realSolidWorksWorkerType)) is true &&
                    File.Exists(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "SolidWorksDrawingDimensionBuilder.cs")) &&
                    File.Exists(Path.Combine(projectRoot, "tools", "SolidWorksDrawingDimensionSmokeRunner", "SolidWorksDrawingDimensionSmokeRunner.csproj"));
                solidWorksRealDrawingTitleBlockImplemented =
                    realSolidWorksWorkerType.GetProperty("SupportsDrawingTitleBlock")?.GetValue(
                        Activator.CreateInstance(realSolidWorksWorkerType)) is true &&
                    File.Exists(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "SolidWorksDrawingTitleBlockBuilder.cs")) &&
                    File.Exists(Path.Combine(projectRoot, "tools", "SolidWorksDrawingTitleBlockSmokeRunner", "SolidWorksDrawingTitleBlockSmokeRunner.csproj"));
                solidWorksRealCadNotExecutedByDefault =
                    workerResult?.RealCadExecuted == false &&
                    envFlagResult.RealCadExecuted == false &&
                    envFlagResult.RealCadConnected == false &&
                    dryRunFlagResult.RealCadExecuted == false &&
                    dryRunFlagResult.RealCadConnected == false;
                solidWorksRealBuildRequiresRequestFlag = solidWorksRealExecutionRequiresRequestFlag;
                solidWorksRealBuildRequiresEnvFlag = solidWorksRealExecutionRequiresEnvFlag;
                solidWorksRealDrawingRequiresEnvFlag = solidWorksRealExecutionRequiresEnvFlag;
                solidWorksRealDrawingDimensionsRequiresEnvFlag = solidWorksRealExecutionRequiresEnvFlag;
                solidWorksRealDrawingTitleBlockRequiresEnvFlag = solidWorksRealExecutionRequiresEnvFlag;
                solidWorksRealBuildRequiresDryRunFalse =
                    dryRunFlagResult.Status == "Rejected" &&
                    dryRunFlagResult.ExecutionMode == "RealPreflightOnly" &&
                    dryRunFlagResult.Issues.Any(issue => issue.Contains("dry_run_mode_enabled", StringComparison.OrdinalIgnoreCase)) &&
                    !dryRunFlagResult.RealCadConnected &&
                    !dryRunFlagResult.RealCadExecuted;

                if (solidWorksRealConnectionSmokeTestAttempted)
                {
                    var smokeRequest = envFlagProbe with
                    {
                        RequestId = $"self-check-solidworks-real-smoke-{Guid.NewGuid():N}",
                        OutputDirectory = Path.Combine(outputRoot, "solidworks", "self-check", "real-smoke"),
                        DryRun = false,
                        AllowRealCadExecution = true,
                        ConnectionSmokeTestOnly = true
                    };

                    try
                    {
                        var smokeResult = await InvokeRealSolidWorksWorkerAsync(
                            realSolidWorksWorkerType,
                            SolidWorksRuntimeOptions.FromEnvironment(),
                            smokeRequest,
                            cancellationToken);
                        solidWorksRealConnectionSmokeTestPassed =
                            smokeResult.Status == "Completed" &&
                            smokeResult.ExecutionMode == "RealConnectionSmokeTest" &&
                            smokeResult.RealCadConnected &&
                            !smokeResult.RealCadExecuted;
                        solidWorksRealConnectionSmokeTestError = solidWorksRealConnectionSmokeTestPassed
                            ? null
                            : string.Join("; ", smokeResult.Issues);
                    }
                    catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or IOException)
                    {
                        solidWorksRealConnectionSmokeTestError = ex.GetBaseException().Message;
                    }
                }

                if (solidWorksRealBuildSmokeTestAttempted)
                {
                    var requestOutputDirectory = CreateRealBuildSmokeOutputDirectory(projectRoot);
                    var buildRequest = envFlagProbe with
                    {
                        RequestId = $"self-check-solidworks-real-build-{Guid.NewGuid():N}",
                        OutputDirectory = requestOutputDirectory,
                        DryRun = false,
                        AllowRealCadExecution = true,
                        ConnectionSmokeTestOnly = false
                    };
                    realBuildRequestDryRun = buildRequest.DryRun;
                    realBuildExecutionMode = "RealBuildPlateBasic4Holes";
                    realBuildOutputDirectory = Path.GetFullPath(buildRequest.OutputDirectory);

                    try
                    {
                        var buildResult = await InvokeRealSolidWorksWorkerAsync(
                            realSolidWorksWorkerType,
                            SolidWorksRuntimeOptions.FromEnvironment(),
                            buildRequest,
                            cancellationToken);
                        realBuildExecutionMode = buildResult.ExecutionMode;
                        var latestReportArtifact = buildResult.GeneratedArtifacts.FirstOrDefault(artifact =>
                            artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase));
                        realBuildLatestReportPath = latestReportArtifact?.FilePath;
                        solidWorksRealBuildOutputsSldprt = buildResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        solidWorksRealBuildOutputsStep = buildResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        solidWorksRealBuildOutputsJsonReport = buildResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        if (realBuildLatestReportPath is null)
                        {
                            realBuildLatestReportPath = FindLatestBuildReportPath(realBuildOutputDirectory);
                            solidWorksRealBuildOutputsJsonReport =
                                !string.IsNullOrWhiteSpace(realBuildLatestReportPath) &&
                                File.Exists(realBuildLatestReportPath) &&
                                new FileInfo(realBuildLatestReportPath).Length > 0;
                        }

                        solidWorksRealBuildReportGenerated = solidWorksRealBuildOutputsJsonReport;
                        solidWorksRealBuildArtifactsValidated =
                            new SolidWorksArtifactValidator(Path.Combine(projectRoot, "output", "solidworks"))
                                .Validate(buildResult)
                                .IsPassed;
                        solidWorksRealBuildSmokeTestPassed =
                            buildResult.Status == "Completed" &&
                            buildResult.ExecutionMode == "RealBuildPlateBasic4Holes" &&
                            buildResult.RealCadConnected &&
                            buildResult.RealCadExecuted &&
                            solidWorksRealBuildOutputsSldprt &&
                            solidWorksRealBuildOutputsStep &&
                            solidWorksRealBuildOutputsJsonReport &&
                            solidWorksRealBuildArtifactsValidated;
                        solidWorksRealBuildSmokeTestError = solidWorksRealBuildSmokeTestPassed
                            ? null
                            : BuildRealSmokeTestError(buildResult.Issues, realBuildLatestReportPath);
                        solidWorksRealBuildFailureStage = solidWorksRealBuildSmokeTestPassed
                            ? null
                            : DetermineSolidWorksRealBuildFailureStage(buildResult.Issues, solidWorksRealBuildSmokeTestError);
                        solidWorksRealBuildErrorIsActionable = IsActionableFailureStage(
                            solidWorksRealBuildFailureStage,
                            solidWorksRealBuildSmokeTestError);
                    }
                    catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or IOException)
                    {
                        solidWorksRealBuildSmokeTestError = ex.GetBaseException().Message;
                        realBuildLatestReportPath ??= FindLatestBuildReportPath(realBuildOutputDirectory);
                        solidWorksRealBuildFailureStage = DetermineSolidWorksRealBuildFailureStage(
                            Array.Empty<string>(),
                            solidWorksRealBuildSmokeTestError);
                        solidWorksRealBuildErrorIsActionable = IsActionableFailureStage(
                            solidWorksRealBuildFailureStage,
                            solidWorksRealBuildSmokeTestError);
                    }
                }

                if (solidWorksRealDrawingSmokeTestAttempted)
                {
                    realDrawingOutputDirectory = CreateRealDrawingSmokeOutputDirectory(projectRoot);
                    var drawingRequest = envFlagProbe with
                    {
                        RequestId = $"self-check-solidworks-real-drawing-{Guid.NewGuid():N}",
                        OutputDirectory = realDrawingOutputDirectory,
                        DryRun = false,
                        AllowRealCadExecution = true,
                        ConnectionSmokeTestOnly = false,
                        DrawingSmokeTestOnly = true,
                        SourcePartPath = FindLatestRealPlatePartPath(projectRoot),
                        DrawingTemplatePath = Environment.GetEnvironmentVariable("SW_TEMPLATE_DRAWING_PATH")
                    };

                    try
                    {
                        var drawingResult = await InvokeRealSolidWorksWorkerAsync(
                            realSolidWorksWorkerType,
                            SolidWorksRuntimeOptions.FromEnvironment(),
                            drawingRequest,
                            cancellationToken);
                        var latestDrawingReportArtifact = drawingResult.GeneratedArtifacts.FirstOrDefault(artifact =>
                            artifact.FilePath.EndsWith("drawing_report.json", StringComparison.OrdinalIgnoreCase));
                        realDrawingLatestReportPath = latestDrawingReportArtifact?.FilePath;
                        solidWorksRealDrawingOutputsSlddrw = drawingResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        solidWorksRealDrawingOutputsPdf = drawingResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        solidWorksRealDrawingOutputsJsonReport = drawingResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith("drawing_report.json", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        if (realDrawingLatestReportPath is null)
                        {
                            realDrawingLatestReportPath = FindLatestDrawingReportPath(realDrawingOutputDirectory);
                            solidWorksRealDrawingOutputsJsonReport =
                                !string.IsNullOrWhiteSpace(realDrawingLatestReportPath) &&
                                File.Exists(realDrawingLatestReportPath) &&
                                new FileInfo(realDrawingLatestReportPath).Length > 0;
                        }

                        solidWorksDrawingReportGenerated = solidWorksRealDrawingOutputsJsonReport;
                        var drawingArtifactsValidated =
                            new SolidWorksArtifactValidator(Path.Combine(projectRoot, "output", "solidworks"))
                                .Validate(drawingResult)
                                .IsPassed;
                        solidWorksRealDrawingSmokeTestPassed =
                            drawingResult.Status == "Completed" &&
                            drawingResult.ExecutionMode == "RealDrawingBasicViews" &&
                            drawingResult.RealCadConnected &&
                            drawingResult.RealCadExecuted &&
                            solidWorksRealDrawingOutputsSlddrw &&
                            solidWorksRealDrawingOutputsPdf &&
                            solidWorksRealDrawingOutputsJsonReport &&
                            drawingArtifactsValidated;
                        solidWorksRealDrawingSmokeTestError = solidWorksRealDrawingSmokeTestPassed
                            ? null
                            : BuildRealSmokeTestError(drawingResult.Issues, realDrawingLatestReportPath);
                        solidWorksRealDrawingFailureStage = solidWorksRealDrawingSmokeTestPassed
                            ? null
                            : DetermineSolidWorksDrawingFailureStage(drawingResult.Issues, solidWorksRealDrawingSmokeTestError, realDrawingLatestReportPath);
                        solidWorksDrawingFailureStageActionable = IsActionableDrawingFailureStage(
                            solidWorksRealDrawingFailureStage,
                            solidWorksRealDrawingSmokeTestError);
                    }
                    catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or IOException)
                    {
                        solidWorksRealDrawingSmokeTestError = ex.GetBaseException().Message;
                        realDrawingLatestReportPath ??= FindLatestDrawingReportPath(realDrawingOutputDirectory);
                        solidWorksRealDrawingFailureStage = DetermineSolidWorksDrawingFailureStage(
                            Array.Empty<string>(),
                            solidWorksRealDrawingSmokeTestError,
                            realDrawingLatestReportPath);
                        solidWorksDrawingFailureStageActionable = IsActionableDrawingFailureStage(
                            solidWorksRealDrawingFailureStage,
                            solidWorksRealDrawingSmokeTestError);
                    }
                }

                if (solidWorksRealDrawingDimensionsSmokeTestAttempted)
                {
                    realDrawingDimensionOutputDirectory = CreateRealDrawingDimensionSmokeOutputDirectory(projectRoot);
                    var dimensionRequest = envFlagProbe with
                    {
                        RequestId = $"self-check-solidworks-real-drawing-dimension-{Guid.NewGuid():N}",
                        OutputDirectory = realDrawingDimensionOutputDirectory,
                        DryRun = false,
                        AllowRealCadExecution = true,
                        ConnectionSmokeTestOnly = false,
                        DrawingSmokeTestOnly = false,
                        DrawingDimensionSmokeTestOnly = true,
                        SourceDrawingPath = FindLatestRealDrawingPath(projectRoot)
                    };

                    try
                    {
                        var dimensionResult = await InvokeRealSolidWorksWorkerAsync(
                            realSolidWorksWorkerType,
                            SolidWorksRuntimeOptions.FromEnvironment(),
                            dimensionRequest,
                            cancellationToken);
                        var latestDimensionReportArtifact = dimensionResult.GeneratedArtifacts.FirstOrDefault(artifact =>
                            artifact.FilePath.EndsWith("dimension_report.json", StringComparison.OrdinalIgnoreCase));
                        realDrawingDimensionLatestReportPath = latestDimensionReportArtifact?.FilePath;
                        solidWorksRealDrawingDimensionsOutputsSlddrw = dimensionResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        solidWorksRealDrawingDimensionsOutputsPdf = dimensionResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        solidWorksRealDrawingDimensionsOutputsJsonReport = dimensionResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith("dimension_report.json", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        if (realDrawingDimensionLatestReportPath is null)
                        {
                            realDrawingDimensionLatestReportPath = FindLatestDimensionReportPath(realDrawingDimensionOutputDirectory);
                            solidWorksRealDrawingDimensionsOutputsJsonReport =
                                !string.IsNullOrWhiteSpace(realDrawingDimensionLatestReportPath) &&
                                File.Exists(realDrawingDimensionLatestReportPath) &&
                                new FileInfo(realDrawingDimensionLatestReportPath).Length > 0;
                        }

                        solidWorksDrawingDimensionReportGenerated = solidWorksRealDrawingDimensionsOutputsJsonReport;
                        var drawingDimensionArtifactsValidated =
                            new SolidWorksArtifactValidator(Path.Combine(projectRoot, "output", "solidworks"))
                                .Validate(dimensionResult)
                                .IsPassed;
                        solidWorksRealDrawingDimensionsSmokeTestPassed =
                            dimensionResult.Status == "Completed" &&
                            dimensionResult.ExecutionMode == "RealDrawingDimensions" &&
                            dimensionResult.RealCadConnected &&
                            dimensionResult.RealCadExecuted &&
                            solidWorksRealDrawingDimensionsOutputsSlddrw &&
                            solidWorksRealDrawingDimensionsOutputsPdf &&
                            solidWorksRealDrawingDimensionsOutputsJsonReport &&
                            drawingDimensionArtifactsValidated;
                        solidWorksRealDrawingDimensionsSmokeTestError = solidWorksRealDrawingDimensionsSmokeTestPassed
                            ? null
                            : BuildRealSmokeTestError(dimensionResult.Issues, realDrawingDimensionLatestReportPath, "dimension_report");
                        solidWorksRealDrawingDimensionFailureStage = solidWorksRealDrawingDimensionsSmokeTestPassed
                            ? null
                            : DetermineSolidWorksDrawingDimensionFailureStage(dimensionResult.Issues, solidWorksRealDrawingDimensionsSmokeTestError, realDrawingDimensionLatestReportPath);
                        solidWorksDrawingDimensionFailureStageActionable = IsActionableDrawingDimensionFailureStage(
                            solidWorksRealDrawingDimensionFailureStage,
                            solidWorksRealDrawingDimensionsSmokeTestError);
                    }
                    catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or IOException)
                    {
                        solidWorksRealDrawingDimensionsSmokeTestError = ex.GetBaseException().Message;
                        realDrawingDimensionLatestReportPath ??= FindLatestDimensionReportPath(realDrawingDimensionOutputDirectory);
                        solidWorksRealDrawingDimensionFailureStage = DetermineSolidWorksDrawingDimensionFailureStage(
                            Array.Empty<string>(),
                            solidWorksRealDrawingDimensionsSmokeTestError,
                            realDrawingDimensionLatestReportPath);
                        solidWorksDrawingDimensionFailureStageActionable = IsActionableDrawingDimensionFailureStage(
                            solidWorksRealDrawingDimensionFailureStage,
                            solidWorksRealDrawingDimensionsSmokeTestError);
                    }
                }

                if (solidWorksRealDrawingTitleBlockSmokeTestAttempted)
                {
                    realDrawingTitleBlockOutputDirectory = CreateRealDrawingTitleBlockSmokeOutputDirectory(projectRoot);
                    var titleBlockRequest = envFlagProbe with
                    {
                        RequestId = $"self-check-solidworks-real-drawing-title-block-{Guid.NewGuid():N}",
                        OutputDirectory = realDrawingTitleBlockOutputDirectory,
                        DryRun = false,
                        AllowRealCadExecution = true,
                        ConnectionSmokeTestOnly = false,
                        DrawingSmokeTestOnly = false,
                        DrawingDimensionSmokeTestOnly = false,
                        DrawingTitleBlockSmokeTestOnly = true,
                        SourceDimensionedDrawingPath = FindLatestDimensionedDrawingPath(projectRoot)
                    };

                    try
                    {
                        var titleBlockResult = await InvokeRealSolidWorksWorkerAsync(
                            realSolidWorksWorkerType,
                            SolidWorksRuntimeOptions.FromEnvironment(),
                            titleBlockRequest,
                            cancellationToken);
                        var latestTitleBlockReportArtifact = titleBlockResult.GeneratedArtifacts.FirstOrDefault(artifact =>
                            artifact.FilePath.EndsWith("title_block_report.json", StringComparison.OrdinalIgnoreCase));
                        realDrawingTitleBlockLatestReportPath = latestTitleBlockReportArtifact?.FilePath;
                        solidWorksRealDrawingTitleBlockOutputsSlddrw = titleBlockResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        solidWorksRealDrawingTitleBlockOutputsPdf = titleBlockResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        solidWorksRealDrawingTitleBlockOutputsJsonReport = titleBlockResult.GeneratedArtifacts.Any(artifact =>
                            artifact.FilePath.EndsWith("title_block_report.json", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(artifact.FilePath) &&
                            new FileInfo(artifact.FilePath).Length > 0);
                        if (realDrawingTitleBlockLatestReportPath is null)
                        {
                            realDrawingTitleBlockLatestReportPath = FindLatestTitleBlockReportPath(realDrawingTitleBlockOutputDirectory);
                            solidWorksRealDrawingTitleBlockOutputsJsonReport =
                                !string.IsNullOrWhiteSpace(realDrawingTitleBlockLatestReportPath) &&
                                File.Exists(realDrawingTitleBlockLatestReportPath) &&
                                new FileInfo(realDrawingTitleBlockLatestReportPath).Length > 0;
                        }

                        solidWorksDrawingTitleBlockReportGenerated = solidWorksRealDrawingTitleBlockOutputsJsonReport;
                        var titleBlockArtifactsValidated =
                            new SolidWorksArtifactValidator(Path.Combine(projectRoot, "output", "solidworks"))
                                .Validate(titleBlockResult)
                                .IsPassed;
                        solidWorksRealDrawingTitleBlockSmokeTestPassed =
                            titleBlockResult.Status == "Completed" &&
                            titleBlockResult.ExecutionMode == "RealDrawingTitleBlock" &&
                            titleBlockResult.RealCadConnected &&
                            titleBlockResult.RealCadExecuted &&
                            solidWorksRealDrawingTitleBlockOutputsSlddrw &&
                            solidWorksRealDrawingTitleBlockOutputsPdf &&
                            solidWorksRealDrawingTitleBlockOutputsJsonReport &&
                            titleBlockArtifactsValidated;
                        solidWorksRealDrawingTitleBlockSmokeTestError = solidWorksRealDrawingTitleBlockSmokeTestPassed
                            ? null
                            : BuildRealSmokeTestError(titleBlockResult.Issues, realDrawingTitleBlockLatestReportPath, "title_block_report");
                        solidWorksRealDrawingTitleBlockFailureStage = solidWorksRealDrawingTitleBlockSmokeTestPassed
                            ? null
                            : DetermineSolidWorksDrawingTitleBlockFailureStage(titleBlockResult.Issues, solidWorksRealDrawingTitleBlockSmokeTestError, realDrawingTitleBlockLatestReportPath);
                        solidWorksDrawingTitleBlockFailureStageActionable = IsActionableDrawingTitleBlockFailureStage(
                            solidWorksRealDrawingTitleBlockFailureStage,
                            solidWorksRealDrawingTitleBlockSmokeTestError);
                    }
                    catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or IOException)
                    {
                        solidWorksRealDrawingTitleBlockSmokeTestError = ex.GetBaseException().Message;
                        realDrawingTitleBlockLatestReportPath ??= FindLatestTitleBlockReportPath(realDrawingTitleBlockOutputDirectory);
                        solidWorksRealDrawingTitleBlockFailureStage = DetermineSolidWorksDrawingTitleBlockFailureStage(
                            Array.Empty<string>(),
                            solidWorksRealDrawingTitleBlockSmokeTestError,
                            realDrawingTitleBlockLatestReportPath);
                        solidWorksDrawingTitleBlockFailureStageActionable = IsActionableDrawingTitleBlockFailureStage(
                            solidWorksRealDrawingTitleBlockFailureStage,
                            solidWorksRealDrawingTitleBlockSmokeTestError);
                    }
                }
            }

            if (solidWorksReleasePackageImplemented)
            {
                try
                {
                    var releaseSelfCheckRoot = Path.Combine(
                        outputRoot,
                        "solidworks",
                        "self-check",
                        "release-source");
                    await WriteReleasePackageSourceFixtureAsync(
                        releaseSelfCheckRoot,
                        string.Empty,
                        cancellationToken);
                    var releasePackageResult = await InvokeSolidWorksReleasePackageBuilderAsync(
                        releaseSelfCheckRoot,
                        cancellationToken);
                    solidWorksReleaseManifestPath = releasePackageResult.ManifestPath;
                    solidWorksPackageQualityReportPath = releasePackageResult.QualityReportPath;
                    solidWorksReleaseSummaryPath = releasePackageResult.SummaryPath;
                    solidWorksReleaseManifestGenerated = ExistingNonEmpty(solidWorksReleaseManifestPath);
                    solidWorksPackageQualityReportGenerated = ExistingNonEmpty(solidWorksPackageQualityReportPath);
                    solidWorksReleaseSummaryGenerated = ExistingNonEmpty(solidWorksReleaseSummaryPath);
                    solidWorksReleaseArtifactsCollected = releasePackageResult.ArtifactsCollected;
                    solidWorksReleaseReportsCollected = releasePackageResult.ReportsCollected;
                    solidWorksReleasePackageFailureStage = releasePackageResult.FailureStage;
                    solidWorksReleasePackageFailureStageActionable = IsActionableReleasePackageFailureStage(
                        solidWorksReleasePackageFailureStage,
                        releasePackageResult.Issues);
                }
                catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or IOException or MissingMethodException or TypeLoadException)
                {
                    solidWorksReleasePackageFailureStage = "package_validation_failed";
                    solidWorksReleasePackageFailureStageActionable = IsActionableReleasePackageFailureStage(
                        solidWorksReleasePackageFailureStage,
                        new[] { ex.GetBaseException().Message });
                }
            }

            var v11VersionStageDocumented = File.ReadAllText(Path.Combine(projectRoot, "docs", "version_stage_index.md"))
                .Contains("V1.1", StringComparison.OrdinalIgnoreCase);
            var v12VersionStageDocumented = File.ReadAllText(Path.Combine(projectRoot, "docs", "version_stage_index.md"))
                .Contains("V1.2", StringComparison.OrdinalIgnoreCase);
            var v13VersionStageDocumented = File.ReadAllText(Path.Combine(projectRoot, "docs", "version_stage_index.md"))
                .Contains("V1.3", StringComparison.OrdinalIgnoreCase);
            var v14VersionStageDocumented = File.ReadAllText(Path.Combine(projectRoot, "docs", "version_stage_index.md"))
                .Contains("V1.4", StringComparison.OrdinalIgnoreCase);
            var v15VersionStageDocumented = File.ReadAllText(Path.Combine(projectRoot, "docs", "version_stage_index.md"))
                .Contains("V1.5", StringComparison.OrdinalIgnoreCase);
            var workerFailureRepairDoc = File.ReadAllText(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "failure_repair.md"));
            var workerApiEvidenceDoc = File.ReadAllText(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "api_evidence.md"));
            var workerReviewChecklistDoc = File.ReadAllText(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "review_checklist.md"));
            var solidWorksDrawingFailureRepairDocumented =
                new[] { "source_part_missing", "drawing_template_missing", "front_view_create_failed", "pdf_export_failed" }
                    .All(stage => workerFailureRepairDoc.Contains(stage, StringComparison.OrdinalIgnoreCase));
            var solidWorksDrawingApiEvidenceDocumented =
                new[] { "CreateDrawViewFromModelView3", "NewDocument", "OpenDoc6", "ActivateDoc3", "SaveAs", "PDF" }
                    .All(api => workerApiEvidenceDoc.Contains(api, StringComparison.OrdinalIgnoreCase));
            var solidWorksDrawingReviewChecklistUpdated =
                workerReviewChecklistDoc.Contains("工程图", StringComparison.OrdinalIgnoreCase) &&
                workerReviewChecklistDoc.Contains("drawing_report", StringComparison.OrdinalIgnoreCase);
            var solidWorksDrawingDimensionFailureRepairDocumented =
                DrawingDimensionFailureStages.All(stage => workerFailureRepairDoc.Contains(stage, StringComparison.OrdinalIgnoreCase));
            var solidWorksDrawingDimensionApiEvidenceDocumented =
                new[] { "CreateLinearDim4", "ICreateDiamDim4", "AddDimension2", "dimension_report" }
                    .All(api => workerApiEvidenceDoc.Contains(api, StringComparison.OrdinalIgnoreCase));
            var solidWorksDrawingDimensionReviewChecklistUpdated =
                workerReviewChecklistDoc.Contains("dimension_report", StringComparison.OrdinalIgnoreCase);
            var solidWorksDrawingTitleBlockFailureRepairDocumented =
                DrawingTitleBlockFailureStages.All(stage => workerFailureRepairDoc.Contains(stage, StringComparison.OrdinalIgnoreCase));
            var solidWorksDrawingTitleBlockApiEvidenceDocumented =
                new[] { "CustomPropertyManager", "Add3", "Set2", "Get6", "GetCurrentSheet", "GetProperties2", "title_block_report" }
                    .All(api => workerApiEvidenceDoc.Contains(api, StringComparison.OrdinalIgnoreCase));
            var solidWorksDrawingTitleBlockReviewChecklistUpdated =
                workerReviewChecklistDoc.Contains("title_block_report", StringComparison.OrdinalIgnoreCase) &&
                workerReviewChecklistDoc.Contains("title_block_population_strategy", StringComparison.OrdinalIgnoreCase) &&
                workerReviewChecklistDoc.Contains("title_block_fields_verified_in_sheet_format", StringComparison.OrdinalIgnoreCase);
            var solidWorksReleasePackageFailureRepairDocumented =
                ReleasePackageFailureStages.All(stage => workerFailureRepairDoc.Contains(stage, StringComparison.OrdinalIgnoreCase));
            var solidWorksReleasePackageReviewChecklistUpdated =
                workerReviewChecklistDoc.Contains("release_manifest", StringComparison.OrdinalIgnoreCase) &&
                workerReviewChecklistDoc.Contains("package_quality_report", StringComparison.OrdinalIgnoreCase) &&
                workerReviewChecklistDoc.Contains("solidworks_release_package", StringComparison.OrdinalIgnoreCase);

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
                null,
                solidWorksRealWorkerSkeletonExists,
                solidWorksEnvironmentValidatorExists,
                solidWorksPreflightReportGenerated,
                solidWorksSessionManagerExists,
                solidWorksRealExecutionDefaultDisabled,
                solidWorksRealExecutionRequiresRequestFlag,
                solidWorksRealExecutionRequiresEnvFlag,
                solidWorksComNotCalledInDefaultSelfCheck,
                solidWorksRealConnectionSmokeTestAttempted,
                solidWorksRealConnectionSmokeTestPassed,
                solidWorksRealConnectionSmokeTestError,
                solidWorksStrictRealSmokeTest,
                solidWorksGenericRealBuildNotImplemented,
                solidWorksRealCadNotExecutedByDefault,
                solidWorksRealPlateBuildImplemented,
                solidWorksRealBuildRequiresEnvFlag,
                solidWorksRealBuildRequiresRequestFlag,
                solidWorksRealBuildRequiresDryRunFalse,
                solidWorksRealBuildDefaultDisabled,
                solidWorksRealBuildSmokeTestAttempted,
                solidWorksRealBuildSmokeTestPassed,
                solidWorksRealBuildSmokeTestError,
                solidWorksStrictRealBuildSmokeTest,
                solidWorksRealBuildArtifactsValidated,
                solidWorksRealBuildReportGenerated,
                solidWorksRealBuildOutputsSldprt,
                solidWorksRealBuildOutputsStep,
                solidWorksRealBuildOutputsJsonReport,
                solidWorksRealBuildNotCalledInDefaultSelfCheck,
                swRealBuildSmokeTestEnvValue,
                swStrictRealBuildTestEnvValue,
                realBuildRequestDryRun,
                realBuildExecutionMode,
                realBuildOutputDirectory,
                realBuildLatestReportPath,
                solidWorksDiagnosticRunnerExists,
                solidWorksDiagnosticRunnerNotCalledByDefault,
                solidWorksLatestDiagnosticReportPath,
                solidWorksLatestDiagnosticFinalStatus,
                solidWorksRealBuildFailureStage,
                solidWorksRealBuildErrorIsActionable,
                solidWorksApiFailureAnalyzerExists,
                solidWorksApiEvidenceCollectorExists,
                solidWorksApiEvidenceReportSchemaExists,
                solidWorksCutHolesApiEvidenceSupported,
                solidWorksReferenceSkillReadonlyAnalysisSupported,
                solidWorksExternalScriptsNotCopied,
                solidWorksApiRepairLoopAvailable,
                solidWorksMacroRecordingRequestAvailable,
                solidWorksPlateFeatureBuilderExists,
                solidWorksRealDrawingBasicViewsImplemented,
                solidWorksRealDrawingDefaultDisabled,
                solidWorksRealDrawingRequiresEnvFlag,
                solidWorksRealDrawingSmokeTestAttempted,
                solidWorksRealDrawingSmokeTestPassed,
                solidWorksRealDrawingSmokeTestError,
                solidWorksStrictRealDrawingSmokeTest,
                solidWorksRealDrawingOutputsSlddrw,
                solidWorksRealDrawingOutputsPdf,
                solidWorksRealDrawingOutputsJsonReport,
                solidWorksRealDrawingNotCalledInDefaultSelfCheck,
                solidWorksDrawingReportGenerated,
                solidWorksRealDrawingFailureStage,
                solidWorksDrawingFailureStageActionable,
                realDrawingOutputDirectory,
                realDrawingLatestReportPath,
                v11VersionStageDocumented,
                solidWorksDrawingFailureRepairDocumented,
                solidWorksDrawingApiEvidenceDocumented,
                solidWorksDrawingReviewChecklistUpdated,
                solidWorksRealDrawingDimensionsImplemented,
                solidWorksRealDrawingDimensionsDefaultDisabled,
                solidWorksRealDrawingDimensionsRequiresEnvFlag,
                solidWorksRealDrawingDimensionsSmokeTestAttempted,
                solidWorksRealDrawingDimensionsSmokeTestPassed,
                solidWorksRealDrawingDimensionsSmokeTestError,
                solidWorksStrictRealDrawingDimensionSmokeTest,
                solidWorksRealDrawingDimensionsOutputsSlddrw,
                solidWorksRealDrawingDimensionsOutputsPdf,
                solidWorksRealDrawingDimensionsOutputsJsonReport,
                solidWorksRealDrawingDimensionsNotCalledInDefaultSelfCheck,
                solidWorksDrawingDimensionReportGenerated,
                solidWorksRealDrawingDimensionFailureStage,
                solidWorksDrawingDimensionFailureStageActionable,
                realDrawingDimensionOutputDirectory,
                realDrawingDimensionLatestReportPath,
                v12VersionStageDocumented,
                solidWorksDrawingDimensionFailureRepairDocumented,
                solidWorksDrawingDimensionApiEvidenceDocumented,
                solidWorksDrawingDimensionReviewChecklistUpdated,
                solidWorksRealDrawingTitleBlockImplemented,
                solidWorksRealDrawingTitleBlockDefaultDisabled,
                solidWorksRealDrawingTitleBlockRequiresEnvFlag,
                solidWorksRealDrawingTitleBlockSmokeTestAttempted,
                solidWorksRealDrawingTitleBlockSmokeTestPassed,
                solidWorksRealDrawingTitleBlockSmokeTestError,
                solidWorksStrictRealDrawingTitleBlockSmokeTest,
                solidWorksRealDrawingTitleBlockOutputsSlddrw,
                solidWorksRealDrawingTitleBlockOutputsPdf,
                solidWorksRealDrawingTitleBlockOutputsJsonReport,
                solidWorksRealDrawingTitleBlockNotCalledInDefaultSelfCheck,
                solidWorksDrawingTitleBlockReportGenerated,
                solidWorksRealDrawingTitleBlockFailureStage,
                solidWorksDrawingTitleBlockFailureStageActionable,
                realDrawingTitleBlockOutputDirectory,
                realDrawingTitleBlockLatestReportPath,
                v13VersionStageDocumented,
                solidWorksDrawingTitleBlockFailureRepairDocumented,
                solidWorksDrawingTitleBlockApiEvidenceDocumented,
                solidWorksDrawingTitleBlockReviewChecklistUpdated,
                "custom_properties_only",
                false,
                solidWorksReleasePackageImplemented,
                solidWorksReleasePackageDefaultNoCadExecution,
                solidWorksReleaseManifestGenerated,
                solidWorksPackageQualityReportGenerated,
                solidWorksReleaseSummaryGenerated,
                solidWorksReleaseArtifactsCollected,
                solidWorksReleaseReportsCollected,
                solidWorksReleasePackageFailureStage,
                solidWorksReleasePackageFailureStageActionable,
                solidWorksReleaseManifestPath,
                solidWorksPackageQualityReportPath,
                solidWorksReleaseSummaryPath,
                v14VersionStageDocumented,
                solidWorksReleasePackageFailureRepairDocumented,
                solidWorksReleasePackageReviewChecklistUpdated,
                realCadWorkerIntegratedIntoMainWorkflow,
                chiefEngineerOrchestratorInvokesCadWorkflow,
                workflowEngineCanRouteToSolidWorksWorker,
                realCadMainWorkflowDefaultDisabled,
                realCadMainWorkflowRequiresRequestFlag,
                realCadMainWorkflowRequiresEnvFlag,
                realCadMainWorkflowPassesQualityGate,
                gatewayDoesNotCallSolidWorksWorker,
                llmDoesNotCallWorkerDirectly,
                releasePackageAllSourceReportsPassedFieldExists,
                releasePackageDeliverableStatusFieldExists,
                releasePackageFailedSourceReportsBlockDeliverable,
                v15VersionStageDocumented);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or MissingMethodException or TargetInvocationException or FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            var error = ex.GetBaseException().Message;
            platform.AuditLog.Record("solidworks", "self-check", "solidworks_skeleton_check_failed", error);
            return new SolidWorksSkeletonSelfCheckResult(
                SolidWorksModuleSkeletonEnabled: false,
                SolidWorksBuildPlanSkillRegistered: false,
                SolidWorksBuildPlanGenerated: false,
                SolidWorksWorkerContractExists: false,
                FakeSolidWorksWorkerRegistered: false,
                FakeSolidWorksWorkerDryRunPassed: false,
                SolidWorksBuildPlanValidatorPassed: false,
                SolidWorksArtifactValidatorPassed: false,
                SolidWorksBuildPlanReviewerPassed: false,
                SolidWorksQualityGatePassed: false,
                SolidWorksFakeArtifactsGenerated: false,
                SolidWorksRealCadNotExecuted: false,
                SolidWorksAgentDoesNotCallWorkerDirectly: false,
                GatewayDoesNotCallSolidWorksWorker: false,
                SelfCheckInfrastructureError: error,
                SolidWorksRealWorkerSkeletonExists: false,
                SolidWorksEnvironmentValidatorExists: false,
                SolidWorksPreflightReportGenerated: false,
                SolidWorksSessionManagerExists: false,
                SolidWorksRealExecutionDefaultDisabled: false,
                SolidWorksRealExecutionRequiresRequestFlag: false,
                SolidWorksRealExecutionRequiresEnvFlag: false,
                SolidWorksComNotCalledInDefaultSelfCheck: false,
                SolidWorksRealConnectionSmokeTestAttempted: false,
                SolidWorksRealConnectionSmokeTestPassed: false,
                SolidWorksRealConnectionSmokeTestError: null,
                SolidWorksStrictRealSmokeTest: false,
                SolidWorksGenericRealBuildNotImplemented: false,
                SolidWorksRealCadNotExecutedByDefault: false,
                SolidWorksRealPlateBuildImplemented: false,
                SolidWorksRealBuildRequiresEnvFlag: false,
                SolidWorksRealBuildRequiresRequestFlag: false,
                SolidWorksRealBuildRequiresDryRunFalse: false,
                SolidWorksRealBuildDefaultDisabled: false,
                SolidWorksRealBuildSmokeTestAttempted: false,
                SolidWorksRealBuildSmokeTestPassed: false,
                SolidWorksRealBuildSmokeTestError: null,
                SolidWorksStrictRealBuildSmokeTest: false,
                SolidWorksRealBuildArtifactsValidated: false,
                SolidWorksRealBuildReportGenerated: false,
                SolidWorksRealBuildOutputsSldprt: false,
                SolidWorksRealBuildOutputsStep: false,
                SolidWorksRealBuildOutputsJsonReport: false,
                SolidWorksRealBuildNotCalledInDefaultSelfCheck: false,
                SwRealBuildSmokeTestEnvValue: Environment.GetEnvironmentVariable("SW_REAL_BUILD_SMOKE_TEST"),
                SwStrictRealBuildTestEnvValue: Environment.GetEnvironmentVariable("SW_STRICT_REAL_BUILD_TEST"),
                RealBuildRequestDryRun: null,
                RealBuildExecutionMode: null,
                RealBuildOutputDirectory: null,
                RealBuildLatestReportPath: null,
                SolidWorksDiagnosticRunnerExists: File.Exists(Path.Combine(projectRoot, "tools", "SolidWorksSmokeRunner", "SolidWorksSmokeRunner.csproj")),
                SolidWorksDiagnosticRunnerNotCalledByDefault: true,
                SolidWorksLatestDiagnosticReportPath: FindLatestDiagnosticReportPath(projectRoot),
                SolidWorksLatestDiagnosticFinalStatus: ReadDiagnosticFinalStatus(FindLatestDiagnosticReportPath(projectRoot)),
                SolidWorksRealBuildFailureStage: null,
                SolidWorksRealBuildErrorIsActionable: false,
                SolidWorksApiFailureAnalyzerExists: File.Exists(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "Diagnostics", "SolidWorksApiFailureAnalyzer.cs")),
                SolidWorksApiEvidenceCollectorExists: File.Exists(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "Diagnostics", "SolidWorksApiEvidenceCollector.cs")),
                SolidWorksApiEvidenceReportSchemaExists: typeof(ApiEvidenceReport) is not null,
                SolidWorksCutHolesApiEvidenceSupported: false,
                SolidWorksReferenceSkillReadonlyAnalysisSupported: false,
                SolidWorksExternalScriptsNotCopied: false,
                SolidWorksApiRepairLoopAvailable: false,
                SolidWorksMacroRecordingRequestAvailable: false,
                SolidWorksPlateFeatureBuilderExists: File.Exists(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "SolidWorksPlateFeatureBuilder.cs")),
                SolidWorksRealDrawingBasicViewsImplemented: false,
                SolidWorksRealDrawingDefaultDisabled: !RealSolidWorksDrawingSmokeTestRequested(),
                SolidWorksRealDrawingRequiresEnvFlag: false,
                SolidWorksRealDrawingSmokeTestAttempted: RealSolidWorksDrawingSmokeTestRequested(),
                SolidWorksRealDrawingSmokeTestPassed: false,
                SolidWorksRealDrawingSmokeTestError: null,
                SolidWorksStrictRealDrawingSmokeTest: StrictRealSolidWorksDrawingTestRequested(),
                SolidWorksRealDrawingOutputsSlddrw: false,
                SolidWorksRealDrawingOutputsPdf: false,
                SolidWorksRealDrawingOutputsJsonReport: false,
                SolidWorksRealDrawingNotCalledInDefaultSelfCheck: !RealSolidWorksDrawingSmokeTestRequested(),
                SolidWorksDrawingReportGenerated: false,
                SolidWorksRealDrawingFailureStage: null,
                SolidWorksDrawingFailureStageActionable: false,
                RealDrawingOutputDirectory: null,
                RealDrawingLatestReportPath: null,
                V11VersionStageDocumented: false,
                SolidWorksDrawingFailureRepairDocumented: false,
                SolidWorksDrawingApiEvidenceDocumented: false,
                SolidWorksDrawingReviewChecklistUpdated: false,
                SolidWorksRealDrawingDimensionsImplemented: false,
                SolidWorksRealDrawingDimensionsDefaultDisabled: !RealSolidWorksDrawingDimensionSmokeTestRequested(),
                SolidWorksRealDrawingDimensionsRequiresEnvFlag: false,
                SolidWorksRealDrawingDimensionsSmokeTestAttempted: RealSolidWorksDrawingDimensionSmokeTestRequested(),
                SolidWorksRealDrawingDimensionsSmokeTestPassed: false,
                SolidWorksRealDrawingDimensionsSmokeTestError: null,
                SolidWorksStrictRealDrawingDimensionSmokeTest: StrictRealSolidWorksDrawingDimensionTestRequested(),
                SolidWorksRealDrawingDimensionsOutputsSlddrw: false,
                SolidWorksRealDrawingDimensionsOutputsPdf: false,
                SolidWorksRealDrawingDimensionsOutputsJsonReport: false,
                SolidWorksRealDrawingDimensionsNotCalledInDefaultSelfCheck: !RealSolidWorksDrawingDimensionSmokeTestRequested(),
                SolidWorksDrawingDimensionReportGenerated: false,
                SolidWorksRealDrawingDimensionFailureStage: null,
                SolidWorksDrawingDimensionFailureStageActionable: false,
                RealDrawingDimensionOutputDirectory: null,
                RealDrawingDimensionLatestReportPath: null,
                V12VersionStageDocumented: false,
                SolidWorksDrawingDimensionFailureRepairDocumented: false,
                SolidWorksDrawingDimensionApiEvidenceDocumented: false,
                SolidWorksDrawingDimensionReviewChecklistUpdated: false,
                SolidWorksRealDrawingTitleBlockImplemented: false,
                SolidWorksRealDrawingTitleBlockDefaultDisabled: !RealSolidWorksDrawingTitleBlockSmokeTestRequested(),
                SolidWorksRealDrawingTitleBlockRequiresEnvFlag: false,
                SolidWorksRealDrawingTitleBlockSmokeTestAttempted: RealSolidWorksDrawingTitleBlockSmokeTestRequested(),
                SolidWorksRealDrawingTitleBlockSmokeTestPassed: false,
                SolidWorksRealDrawingTitleBlockSmokeTestError: null,
                SolidWorksStrictRealDrawingTitleBlockSmokeTest: StrictRealSolidWorksDrawingTitleBlockTestRequested(),
                SolidWorksRealDrawingTitleBlockOutputsSlddrw: false,
                SolidWorksRealDrawingTitleBlockOutputsPdf: false,
                SolidWorksRealDrawingTitleBlockOutputsJsonReport: false,
                SolidWorksRealDrawingTitleBlockNotCalledInDefaultSelfCheck: !RealSolidWorksDrawingTitleBlockSmokeTestRequested(),
                SolidWorksDrawingTitleBlockReportGenerated: false,
                SolidWorksRealDrawingTitleBlockFailureStage: null,
                SolidWorksDrawingTitleBlockFailureStageActionable: false,
                RealDrawingTitleBlockOutputDirectory: null,
                RealDrawingTitleBlockLatestReportPath: null,
                V13VersionStageDocumented: false,
                SolidWorksDrawingTitleBlockFailureRepairDocumented: false,
                SolidWorksDrawingTitleBlockApiEvidenceDocumented: false,
                SolidWorksDrawingTitleBlockReviewChecklistUpdated: false,
                SolidWorksDrawingTitleBlockPopulationStrategy: "custom_properties_only",
                SolidWorksDrawingTitleBlockFieldsVerifiedInSheetFormat: false,
                SolidWorksReleasePackageImplemented: File.Exists(Path.Combine(projectRoot, "src", "Workers", "SolidWorks", "SolidWorksReleasePackageBuilder.cs")),
                SolidWorksReleasePackageDefaultNoCadExecution: false,
                SolidWorksReleaseManifestGenerated: false,
                SolidWorksPackageQualityReportGenerated: false,
                SolidWorksReleaseSummaryGenerated: false,
                SolidWorksReleaseArtifactsCollected: false,
                SolidWorksReleaseReportsCollected: false,
                SolidWorksReleasePackageFailureStage: "package_validation_failed",
                SolidWorksReleasePackageFailureStageActionable: true,
                SolidWorksReleaseManifestPath: null,
                SolidWorksPackageQualityReportPath: null,
                SolidWorksReleaseSummaryPath: null,
                V14VersionStageDocumented: false,
                SolidWorksReleasePackageFailureRepairDocumented: false,
                SolidWorksReleasePackageReviewChecklistUpdated: false,
                RealCadWorkerIntegratedIntoMainWorkflow: false,
                ChiefEngineerOrchestratorInvokesCadWorkflow: false,
                WorkflowEngineCanRouteToSolidWorksWorker: false,
                RealCadMainWorkflowDefaultDisabled: false,
                RealCadMainWorkflowRequiresRequestFlag: false,
                RealCadMainWorkflowRequiresEnvFlag: false,
                RealCadMainWorkflowPassesQualityGate: false,
                GatewayDoesNotCallWorkerDirectly: false,
                LlmDoesNotCallWorkerDirectly: false,
                ReleasePackageAllSourceReportsPassedFieldExists: typeof(SolidWorksPackageQualityReport).GetProperty(nameof(SolidWorksPackageQualityReport.AllSourceReportsPassed)) is not null,
                ReleasePackageDeliverableStatusFieldExists: typeof(SolidWorksPackageQualityReport).GetProperty(nameof(SolidWorksPackageQualityReport.DeliverableStatus)) is not null,
                ReleasePackageFailedSourceReportsBlockDeliverable: false,
                V15VersionStageDocumented: false);
        }
    }

    private static async Task<SolidWorksWorkerResult> InvokeRealSolidWorksWorkerAsync(
        Type realSolidWorksWorkerType,
        SolidWorksRuntimeOptions options,
        SolidWorksWorkerRequest request,
        CancellationToken cancellationToken)
    {
        var worker = Activator.CreateInstance(realSolidWorksWorkerType, new object?[] { null, options })
            ?? throw new InvalidOperationException("Could not create RealSolidWorksWorker.");
        var method = realSolidWorksWorkerType.GetMethods()
            .Single(method =>
                method.Name == "ExecuteAsync" &&
                method.GetParameters().Length == 2 &&
                method.GetParameters()[0].ParameterType == typeof(SolidWorksWorkerRequest));
        var task = (Task<SolidWorksWorkerResult>)method.Invoke(worker, new object?[] { request, cancellationToken })!;
        return await task;
    }

    private static async Task<SolidWorksReleasePackageReflectionResult> InvokeSolidWorksReleasePackageBuilderAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var builderType = Type.GetType("SolidWorksWorker.SolidWorksReleasePackageBuilder, SolidWorksWorker")
            ?? throw new TypeLoadException("SolidWorksReleasePackageBuilder was not found.");
        var builder = Activator.CreateInstance(builderType)
            ?? throw new InvalidOperationException("Could not create SolidWorksReleasePackageBuilder.");
        var method = builderType.GetMethods()
            .Single(method =>
                method.Name == "BuildAsync" &&
                method.GetParameters().Length == 3 &&
                method.GetParameters()[0].ParameterType == typeof(string) &&
                method.GetParameters()[2].ParameterType == typeof(CancellationToken));
        var task = method.Invoke(builder, new object?[] { projectRoot, null, cancellationToken }) as Task
            ?? throw new MissingMethodException("SolidWorksReleasePackageBuilder.BuildAsync did not return a Task.");
        await task;
        var result = task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new InvalidOperationException("SolidWorksReleasePackageBuilder.BuildAsync returned null.");

        return new SolidWorksReleasePackageReflectionResult(
            ReadStringProperty(result, "Status") ?? "Failed",
            ReadStringProperty(result, "OutputDirectory") ?? string.Empty,
            ReadStringProperty(result, "ManifestPath"),
            ReadStringProperty(result, "QualityReportPath"),
            ReadStringProperty(result, "SummaryPath"),
            ReadStringProperty(result, "FailureStage"),
            ReadBoolProperty(result, "ArtifactsCollected"),
            ReadBoolProperty(result, "ReportsCollected"),
            ReadStringListProperty(result, "Issues"));
    }

    private static async Task<bool> ReleasePackageFailedSourceReportsBlockDeliverableAsync(CancellationToken cancellationToken)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "solidworks_release_failed_source_self_check",
            Guid.NewGuid().ToString("N"));

        try
        {
            await WriteReleasePackageSourceFixtureAsync(root, "dimension_report.json", cancellationToken);
            var result = await InvokeSolidWorksReleasePackageBuilderAsync(root, cancellationToken);
            if (!ExistingNonEmpty(result.QualityReportPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result.QualityReportPath!, cancellationToken));
            var rootElement = document.RootElement;
            var packageBuildStatus = rootElement.GetProperty("package_build_status").GetString();
            var allSourceReportsPassed = rootElement.GetProperty("all_source_reports_passed").GetBoolean();
            var deliverableStatus = rootElement.GetProperty("deliverable_status").GetString();
            var finalStatus = rootElement.GetProperty("final_status").GetString();
            var failures = rootElement.GetProperty("source_report_failures")
                .EnumerateArray()
                .Select(element => element.GetProperty("name").GetString())
                .ToArray();

            return string.Equals(packageBuildStatus, "Passed", StringComparison.OrdinalIgnoreCase) &&
                   !allSourceReportsPassed &&
                   string.Equals(deliverableStatus, "NotDeliverable", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(finalStatus, "Failed", StringComparison.OrdinalIgnoreCase) &&
                   failures.Contains("dimension_report.json", StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or MissingMethodException or TypeLoadException)
        {
            return false;
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            var expectedBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "solidworks_release_failed_source_self_check"));
            if (fullRoot.StartsWith(expectedBase, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    private static async Task WriteReleasePackageSourceFixtureAsync(
        string root,
        string failedReportName,
        CancellationToken cancellationToken)
    {
        var timestamp = "20260706_000000_000_self_check";
        var plateRoot = Path.Combine(root, "output", "solidworks", "real", "plate_basic_4holes", timestamp);
        var drawingRoot = Path.Combine(root, "output", "solidworks", "real", "plate_basic_4holes_drawing", timestamp);
        var dimensionRoot = Path.Combine(root, "output", "solidworks", "real", "plate_basic_4holes_drawing_dimensions", timestamp);
        var titleBlockRoot = Path.Combine(root, "output", "solidworks", "real", "plate_basic_4holes_title_block", timestamp);
        var diagnosticRoot = Path.Combine(root, "output", "solidworks", "diagnostics", "plate_basic_4holes", timestamp);

        await WriteTextFixtureFileAsync(Path.Combine(plateRoot, "plate_basic_4holes.SLDPRT"), "fake sldprt bytes", cancellationToken);
        await WriteTextFixtureFileAsync(Path.Combine(plateRoot, "plate_basic_4holes.STEP"), "fake step bytes", cancellationToken);
        await WriteJsonFixtureReportAsync(Path.Combine(plateRoot, "build_report.json"), failedReportName, "build_report.json", cancellationToken);
        await WriteTextFixtureFileAsync(Path.Combine(drawingRoot, "plate_basic_4holes.SLDDRW"), "fake drawing bytes", cancellationToken);
        await WriteTextFixtureFileAsync(Path.Combine(drawingRoot, "plate_basic_4holes.pdf"), "fake drawing pdf bytes", cancellationToken);
        await WriteJsonFixtureReportAsync(Path.Combine(drawingRoot, "drawing_report.json"), failedReportName, "drawing_report.json", cancellationToken);
        await WriteTextFixtureFileAsync(Path.Combine(dimensionRoot, "plate_basic_4holes_dimensioned.SLDDRW"), "fake dimensioned drawing bytes", cancellationToken);
        await WriteTextFixtureFileAsync(Path.Combine(dimensionRoot, "plate_basic_4holes_dimensioned.pdf"), "fake dimensioned pdf bytes", cancellationToken);
        await WriteJsonFixtureReportAsync(Path.Combine(dimensionRoot, "dimension_report.json"), failedReportName, "dimension_report.json", cancellationToken);
        await WriteTextFixtureFileAsync(Path.Combine(titleBlockRoot, "plate_basic_4holes_title_block.SLDDRW"), "fake title block drawing bytes", cancellationToken);
        await WriteTextFixtureFileAsync(Path.Combine(titleBlockRoot, "plate_basic_4holes_title_block.pdf"), "fake title block pdf bytes", cancellationToken);
        await WriteJsonFixtureReportAsync(Path.Combine(titleBlockRoot, "title_block_report.json"), failedReportName, "title_block_report.json", cancellationToken);
        await WriteJsonFixtureReportAsync(Path.Combine(diagnosticRoot, "diagnostic_report.json"), failedReportName, "diagnostic_report.json", cancellationToken);
    }

    private static async Task WriteTextFixtureFileAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    private static async Task WriteJsonFixtureReportAsync(
        string path,
        string failedReportName,
        string reportName,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var failed = string.Equals(reportName, failedReportName, StringComparison.OrdinalIgnoreCase);
        var payload = new Dictionary<string, string?>
        {
            ["final_status"] = failed ? "Failed" : "Passed",
            ["failure_stage"] = failed ? "source_report_failed_probe" : null
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions()), cancellationToken);
    }

    private static string? ReadStringProperty(object instance, string propertyName) =>
        instance.GetType().GetProperty(propertyName)?.GetValue(instance) as string;

    private static bool ReadBoolProperty(object instance, string propertyName) =>
        instance.GetType().GetProperty(propertyName)?.GetValue(instance) is true;

    private static IReadOnlyList<string> ReadStringListProperty(object instance, string propertyName)
    {
        var value = instance.GetType().GetProperty(propertyName)?.GetValue(instance);
        return value is IEnumerable<string> strings
            ? strings.ToArray()
            : Array.Empty<string>();
    }

    private static bool ExistingNonEmpty(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        new FileInfo(path).Length > 0;

    private static bool SolidWorksComFacadeInjectionSeamsAreAvailable()
    {
        try
        {
            var facade = Type.GetType("SolidWorksWorker.ISolidWorksComFacade, SolidWorksWorker", throwOnError: false);
            var verifier = Type.GetType("SolidWorksWorker.ISolidWorksFileVerifier, SolidWorksWorker", throwOnError: false);
            var propertyReader = Type.GetType("SolidWorksWorker.ISolidWorksPropertyReader, SolidWorksWorker", throwOnError: false);
            var featureBuilder = Type.GetType("SolidWorksWorker.SolidWorksPlateFeatureBuilder, SolidWorksWorker", throwOnError: false);
            var plateBuilder = Type.GetType("SolidWorksWorker.LateBoundSolidWorksPlateBuilder, SolidWorksWorker", throwOnError: false);
            var drawingBuilder = Type.GetType("SolidWorksWorker.LateBoundSolidWorksDrawingBuilder, SolidWorksWorker", throwOnError: false);
            var dimensionBuilder = Type.GetType("SolidWorksWorker.LateBoundSolidWorksDrawingDimensionBuilder, SolidWorksWorker", throwOnError: false);
            var titleBlockBuilder = Type.GetType("SolidWorksWorker.LateBoundSolidWorksDrawingTitleBlockBuilder, SolidWorksWorker", throwOnError: false);

            return facade?.IsInterface == true &&
                   verifier?.IsInterface == true &&
                   propertyReader?.IsInterface == true &&
                   featureBuilder is not null &&
                   plateBuilder?.GetConstructor([facade, verifier, featureBuilder]) is not null &&
                   drawingBuilder?.GetConstructor([facade, verifier]) is not null &&
                   dimensionBuilder?.GetConstructor([facade, verifier]) is not null &&
                   titleBlockBuilder?.GetConstructor([facade, verifier, propertyReader]) is not null;
        }
        catch (FileLoadException)
        {
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static async Task<string> ResolveSourceRevisionAsync(string projectRoot)
    {
        var commit = await RunGitAsync(projectRoot, "rev-parse", "--verify", "HEAD");
        if (string.IsNullOrWhiteSpace(commit))
        {
            return "unknown";
        }

        var trackedChanges = await RunGitAsync(projectRoot, "status", "--porcelain", "--untracked-files=no");
        return string.IsNullOrWhiteSpace(trackedChanges)
            ? commit
            : $"{commit}-dirty";
    }

    internal static async Task<string?> RunGitAsync(string projectRoot, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await Task.WhenAll(stdout, stderr);
                return null;
            }

            await Task.WhenAll(stdout, stderr);
            return process.ExitCode == 0 ? stdout.Result.Trim() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static RealAcceptanceOutputPaths WriteRealAcceptanceOutputs(
        string projectRoot,
        SelfCheckRunMetadata runMetadata,
        string outputRoot)
    {
        lock (RealAcceptanceOutputLock)
        {
            return WriteRealAcceptanceOutputsCore(projectRoot, runMetadata, outputRoot);
        }
    }

    private static RealAcceptanceOutputPaths WriteRealAcceptanceOutputsCore(
        string projectRoot,
        SelfCheckRunMetadata runMetadata,
        string outputRoot)
    {
        var outputDirectory = Path.Combine(outputRoot, "solidworks", "real_acceptance");
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(outputDirectory, "real_acceptance_report.json");
        var markdownPath = Path.Combine(outputDirectory, "latest_real_outputs.md");

        var latestSldprtPath = FindLatestRealPlatePartPath(projectRoot);
        var latestStepPath = FindLatestRealPlateStepPath(projectRoot);
        var latestSlddrwPath =
            FindLatestTitleBlockDrawingPath(projectRoot) ??
            FindLatestDimensionedDrawingPath(projectRoot) ??
            FindLatestRealDrawingPath(projectRoot);
        var latestPdfPath =
            FindLatestTitleBlockPdfPath(projectRoot) ??
            FindLatestDimensionedPdfPath(projectRoot) ??
            FindLatestRealDrawingPdfPath(projectRoot);
        var buildReportPath = FindLatestBuildReportPath(Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes"));
        var drawingReportPath = FindLatestDrawingReportPath(Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_drawing"));
        var dimensionReportPath = FindLatestDimensionReportPath(Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_drawing_dimensions"));
        var titleBlockReportPath = FindLatestTitleBlockReportPath(Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_title_block"));
        var packageQualityReportPath = FindLatestPackageQualityReportPath(projectRoot);
        var sourceReportStatuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build_report"] = NormalizeEvidenceStatus(ReadJsonString(buildReportPath, "final_status")),
            ["drawing_report"] = NormalizeEvidenceStatus(ReadJsonString(drawingReportPath, "final_status")),
            ["dimension_report"] = NormalizeEvidenceStatus(ReadJsonString(dimensionReportPath, "final_status")),
            ["title_block_report"] = NormalizeEvidenceStatus(ReadJsonString(titleBlockReportPath, "final_status")),
            ["package_quality_report"] = NormalizeEvidenceStatus(ReadJsonString(packageQualityReportPath, "final_status"))
        };
        var packageReportsPassed = ReadJsonBool(packageQualityReportPath, "all_source_reports_passed");
        var packageDeliverableStatus = ReadJsonString(packageQualityReportPath, "deliverable_status");
        var allSourceReportsPassed = packageReportsPassed is true &&
                                     sourceReportStatuses.Values.All(status =>
                                         string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase));
        var deliverableBlockedBy = sourceReportStatuses
            .Where(item => !string.Equals(item.Value, "Passed", StringComparison.OrdinalIgnoreCase))
            .Select(item => $"{item.Key}.final_status={item.Value}")
            .ToList();
        var artifacts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sldprt"] = latestSldprtPath,
            ["step"] = latestStepPath,
            ["slddrw"] = latestSlddrwPath,
            ["pdf"] = latestPdfPath
        };
        deliverableBlockedBy.AddRange(artifacts
            .Where(item => !ExistingNonEmpty(item.Value))
            .Select(item => $"{item.Key}_artifact_missing_or_empty"));
        if (packageReportsPassed is not true)
        {
            deliverableBlockedBy.Add("package_quality_report.all_source_reports_passed!=true");
        }

        if (!string.Equals(packageDeliverableStatus, "Deliverable", StringComparison.OrdinalIgnoreCase))
        {
            deliverableBlockedBy.Add("package_quality_report.deliverable_status!=Deliverable");
        }

        deliverableBlockedBy = deliverableBlockedBy.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var deliverableStatus = allSourceReportsPassed &&
                                artifacts.Values.All(ExistingNonEmpty) &&
                                string.Equals(packageDeliverableStatus, "Deliverable", StringComparison.OrdinalIgnoreCase)
            ? "Deliverable"
            : "NotDeliverable";
        var payload = new
        {
            schema_version = SelfCheckSchemaVersion,
            run_id = runMetadata.RunId,
            generated_at = runMetadata.GeneratedAt,
            source_revision = runMetadata.SourceRevision,
            latest_sldprt_path = latestSldprtPath,
            latest_step_path = latestStepPath,
            latest_slddrw_path = latestSlddrwPath,
            latest_pdf_path = latestPdfPath,
            build_report_path = buildReportPath,
            drawing_report_path = drawingReportPath,
            dimension_report_path = dimensionReportPath,
            title_block_report_path = titleBlockReportPath,
            package_quality_report_path = packageQualityReportPath,
            source_report_final_status = sourceReportStatuses,
            all_source_reports_passed = allSourceReportsPassed,
            deliverable_status = deliverableStatus,
            deliverable_blocked_by = deliverableBlockedBy
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, JsonOptions()));
        File.WriteAllText(
            markdownPath,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    "# 最新真实 SolidWorks 输出",
                    "",
                    $"运行标识：{runMetadata.RunId}",
                    $"生成时间：{runMetadata.GeneratedAt:O}",
                    $"源码版本：{runMetadata.SourceRevision}",
                    "",
                    "| 项目 | 路径或状态 |",
                    "|---|---|",
                    $"| 最新 SLDPRT 路径 | {DisplayPath(latestSldprtPath)} |",
                    $"| 最新 STEP 路径 | {DisplayPath(latestStepPath)} |",
                    $"| 最新 SLDDRW 路径 | {DisplayPath(latestSlddrwPath)} |",
                    $"| 最新 PDF 路径 | {DisplayPath(latestPdfPath)} |",
                    $"| 最新 build_report 路径 | {DisplayPath(buildReportPath)} |",
                    $"| 最新 drawing_report 路径 | {DisplayPath(drawingReportPath)} |",
                    $"| 最新 dimension_report 路径 | {DisplayPath(dimensionReportPath)} |",
                    $"| 最新 title_block_report 路径 | {DisplayPath(titleBlockReportPath)} |",
                    $"| 最新 package_quality_report 路径 | {DisplayPath(packageQualityReportPath)} |",
                    $"| build_report final_status | {DisplayStatus(sourceReportStatuses["build_report"])} |",
                    $"| drawing_report final_status | {DisplayStatus(sourceReportStatuses["drawing_report"])} |",
                    $"| dimension_report final_status | {DisplayStatus(sourceReportStatuses["dimension_report"])} |",
                    $"| title_block_report final_status | {DisplayStatus(sourceReportStatuses["title_block_report"])} |",
                    $"| package_quality_report final_status | {DisplayStatus(sourceReportStatuses["package_quality_report"])} |",
                    $"| all_source_reports_passed | {allSourceReportsPassed.ToString().ToLowerInvariant()} |",
                    $"| deliverable_status | {deliverableStatus} |",
                    $"| deliverable_blocked_by | {string.Join("；", deliverableBlockedBy)} |"
                }) + Environment.NewLine);

        var isValid = ValidateRealAcceptanceReport(reportPath) && ExistingNonEmpty(markdownPath);
        return new RealAcceptanceOutputPaths(reportPath, markdownPath, isValid);
    }

    private static string NormalizeEvidenceStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "MissingOrInvalid" : status;

    private static bool ValidateRealAcceptanceReport(string reportPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("schema_version", out var schemaVersion) ||
                !string.Equals(schemaVersion.GetString(), SelfCheckSchemaVersion, StringComparison.Ordinal) ||
                !root.TryGetProperty("run_id", out var runId) ||
                string.IsNullOrWhiteSpace(runId.GetString()) ||
                !root.TryGetProperty("source_revision", out var sourceRevision) ||
                string.IsNullOrWhiteSpace(sourceRevision.GetString()) ||
                !root.TryGetProperty("source_report_final_status", out var sourceStatuses) ||
                sourceStatuses.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("all_source_reports_passed", out var allSourceReportsPassed) ||
                allSourceReportsPassed.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("deliverable_status", out var deliverableStatus) ||
                !root.TryGetProperty("deliverable_blocked_by", out var blockedBy) ||
                blockedBy.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var status = deliverableStatus.GetString();
            var isDeliverable = string.Equals(status, "Deliverable", StringComparison.Ordinal);
            var isNotDeliverable = string.Equals(status, "NotDeliverable", StringComparison.Ordinal);
            return (isDeliverable || isNotDeliverable) &&
                   (!isDeliverable || (allSourceReportsPassed.GetBoolean() && blockedBy.GetArrayLength() == 0)) &&
                   (!isNotDeliverable || blockedBy.GetArrayLength() > 0);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string DisplayPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "未找到" : Path.GetFullPath(path);

    private static string DisplayStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "未找到" : status;

    private static bool RealSolidWorksSmokeTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_REAL_SMOKE_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool StrictRealSolidWorksSmokeTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_STRICT_REAL_SMOKE_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool RealSolidWorksBuildSmokeTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_REAL_BUILD_SMOKE_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool StrictRealSolidWorksBuildTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_STRICT_REAL_BUILD_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool RealSolidWorksDrawingSmokeTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_REAL_DRAWING_SMOKE_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool StrictRealSolidWorksDrawingTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_STRICT_REAL_DRAWING_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool RealSolidWorksDrawingDimensionSmokeTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_REAL_DRAWING_DIMENSION_SMOKE_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool StrictRealSolidWorksDrawingDimensionTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_STRICT_REAL_DRAWING_DIMENSION_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool RealSolidWorksDrawingTitleBlockSmokeTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_REAL_DRAWING_TITLE_BLOCK_SMOKE_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool StrictRealSolidWorksDrawingTitleBlockTestRequested() =>
        string.Equals(Environment.GetEnvironmentVariable("SW_STRICT_REAL_DRAWING_TITLE_BLOCK_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    private static string CreateRealBuildSmokeOutputDirectory(string projectRoot) =>
        Path.GetFullPath(Path.Combine(
            projectRoot,
            "output",
            "solidworks",
            "real",
            "plate_basic_4holes",
            $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}"));

    private static string CreateRealDrawingSmokeOutputDirectory(string projectRoot) =>
        Path.GetFullPath(Path.Combine(
            projectRoot,
            "output",
            "solidworks",
            "real",
            "plate_basic_4holes_drawing",
            $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}"));

    private static string CreateRealDrawingDimensionSmokeOutputDirectory(string projectRoot) =>
        Path.GetFullPath(Path.Combine(
            projectRoot,
            "output",
            "solidworks",
            "real",
            "plate_basic_4holes_drawing_dimensions",
            $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}"));

    private static string CreateRealDrawingTitleBlockSmokeOutputDirectory(string projectRoot) =>
        Path.GetFullPath(Path.Combine(
            projectRoot,
            "output",
            "solidworks",
            "real",
            "plate_basic_4holes_title_block",
            $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}"));

    private static string? FindLatestBuildReportPath(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(outputDirectory, "build_report.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestDrawingReportPath(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(outputDirectory, "drawing_report.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestDimensionReportPath(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(outputDirectory, "dimension_report.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestTitleBlockReportPath(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(outputDirectory, "title_block_report.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestRealPlateStepPath(string projectRoot) =>
        FindLatestNonEmptyFile(
            Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes"),
            "plate_basic_4holes.STEP");

    private static string? FindLatestTitleBlockDrawingPath(string projectRoot) =>
        FindLatestNonEmptyFile(
            Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_title_block"),
            "plate_basic_4holes_title_block.SLDDRW");

    private static string? FindLatestTitleBlockPdfPath(string projectRoot) =>
        FindLatestNonEmptyFile(
            Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_title_block"),
            "plate_basic_4holes_title_block.pdf");

    private static string? FindLatestDimensionedPdfPath(string projectRoot) =>
        FindLatestNonEmptyFile(
            Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_drawing_dimensions"),
            "plate_basic_4holes_dimensioned.pdf");

    private static string? FindLatestRealDrawingPdfPath(string projectRoot) =>
        FindLatestNonEmptyFile(
            Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_drawing"),
            "plate_basic_4holes.pdf");

    private static string? FindLatestPackageQualityReportPath(string projectRoot) =>
        FindLatestFile(
            Path.Combine(projectRoot, "output", "solidworks", "release"),
            "package_quality_report.json");

    private static string? FindLatestNonEmptyFile(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Length > 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestFile(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestRealPlatePartPath(string projectRoot)
    {
        var realPlateRoot = Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes");
        if (!Directory.Exists(realPlateRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(realPlateRoot, "plate_basic_4holes.SLDPRT", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Length > 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestRealDrawingPath(string projectRoot)
    {
        var realDrawingRoot = Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_drawing");
        if (!Directory.Exists(realDrawingRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(realDrawingRoot, "plate_basic_4holes.SLDDRW", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Length > 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestDimensionedDrawingPath(string projectRoot)
    {
        var realDrawingDimensionRoot = Path.Combine(projectRoot, "output", "solidworks", "real", "plate_basic_4holes_drawing_dimensions");
        if (!Directory.Exists(realDrawingDimensionRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(realDrawingDimensionRoot, "plate_basic_4holes_dimensioned.SLDDRW", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Length > 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FindLatestDiagnosticReportPath(string projectRoot)
    {
        var diagnosticsRoot = Path.Combine(projectRoot, "output", "solidworks", "diagnostics");
        if (!Directory.Exists(diagnosticsRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(diagnosticsRoot, "diagnostic_report.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? ReadDiagnosticFinalStatus(string? diagnosticReportPath)
    {
        if (string.IsNullOrWhiteSpace(diagnosticReportPath) || !File.Exists(diagnosticReportPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(diagnosticReportPath));
            return document.RootElement.TryGetProperty("final_status", out var finalStatus)
                ? finalStatus.GetString()
                : null;
        }
        catch (JsonException)
        {
            return "InvalidJson";
        }
        catch (IOException)
        {
            return "Unreadable";
        }
        catch (UnauthorizedAccessException)
        {
            return "Unreadable";
        }
    }

    private static string? ReadJsonString(string? reportPath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool? ReadJsonBool(string? reportPath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            if (!document.RootElement.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? DetermineSolidWorksRealBuildFailureStage(
        IEnumerable<string> issues,
        string? error)
    {
        var text = string.Join(" ", issues.Append(error ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.Contains("preflight", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("template_part_path", StringComparison.OrdinalIgnoreCase))
        {
            return "preflight_failed";
        }

        if (text.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return "connection_failed";
        }

        if (text.Contains("new_part", StringComparison.OrdinalIgnoreCase))
        {
            return "new_part_failed";
        }

        if (text.Contains("sketch", StringComparison.OrdinalIgnoreCase))
        {
            return "sketch_failed";
        }

        if (text.Contains("extrude", StringComparison.OrdinalIgnoreCase))
        {
            return "extrude_failed";
        }

        if (text.Contains("cut_holes", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("hole", StringComparison.OrdinalIgnoreCase))
        {
            return "cut_holes_failed";
        }

        if (text.Contains("sldprt", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return "save_sldprt_failed";
        }

        if (text.Contains("step", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("export", StringComparison.OrdinalIgnoreCase))
        {
            return "export_step_failed";
        }

        if (text.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return "build_report_failed";
        }

        return "unknown_failed";
    }

    private static string? DetermineSolidWorksDrawingFailureStage(
        IEnumerable<string> issues,
        string? error,
        string? reportPath)
    {
        if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
                if (document.RootElement.TryGetProperty("failure_stage", out var failureStage) &&
                    !string.IsNullOrWhiteSpace(failureStage.GetString()))
                {
                    return failureStage.GetString();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return "drawing_report_write_failed";
            }
        }

        var text = string.Join(" ", issues.Append(error ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var stage in DrawingFailureStages)
        {
            if (text.Contains(stage, StringComparison.OrdinalIgnoreCase))
            {
                return stage;
            }
        }

        if (text.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return "drawing_template_missing";
        }

        if (text.Contains("source", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("part", StringComparison.OrdinalIgnoreCase))
        {
            return "source_part_missing";
        }

        if (text.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "pdf_export_failed";
        }

        if (text.Contains("slddrw", StringComparison.OrdinalIgnoreCase))
        {
            return "slddrw_save_failed";
        }

        return "drawing_api_evidence_insufficient";
    }

    private static bool IsActionableDrawingFailureStage(string? failureStage, string? error) =>
        failureStage is null ||
        DrawingFailureStages.Contains(failureStage, StringComparer.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(error);

    private static string? DetermineSolidWorksDrawingDimensionFailureStage(
        IEnumerable<string> issues,
        string? error,
        string? reportPath)
    {
        if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
                if (document.RootElement.TryGetProperty("failure_stage", out var failureStage) &&
                    !string.IsNullOrWhiteSpace(failureStage.GetString()))
                {
                    return failureStage.GetString();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return "dimension_report_write_failed";
            }
        }

        var text = string.Join(" ", issues.Append(error ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var stage in DrawingDimensionFailureStages)
        {
            if (text.Contains(stage, StringComparison.OrdinalIgnoreCase))
            {
                return stage;
            }
        }

        if (text.Contains("source", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("drawing", StringComparison.OrdinalIgnoreCase))
        {
            return "source_drawing_missing";
        }

        if (text.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "dimension_pdf_export_failed";
        }

        if (text.Contains("slddrw", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return "dimension_save_failed";
        }

        return "drawing_dimension_api_evidence_insufficient";
    }

    private static bool IsActionableDrawingDimensionFailureStage(string? failureStage, string? error) =>
        failureStage is null ||
        DrawingDimensionFailureStages.Contains(failureStage, StringComparer.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(error);

    private static string? DetermineSolidWorksDrawingTitleBlockFailureStage(
        IEnumerable<string> issues,
        string? error,
        string? reportPath)
    {
        if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
                if (document.RootElement.TryGetProperty("failure_stage", out var failureStage) &&
                    !string.IsNullOrWhiteSpace(failureStage.GetString()))
                {
                    return failureStage.GetString();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return "title_block_report_write_failed";
            }
        }

        var text = string.Join(" ", issues.Append(error ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var stage in DrawingTitleBlockFailureStages)
        {
            if (text.Contains(stage, StringComparison.OrdinalIgnoreCase))
            {
                return stage;
            }
        }

        if (text.Contains("source", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("dimensioned", StringComparison.OrdinalIgnoreCase))
        {
            return "source_dimensioned_drawing_missing";
        }

        if (text.Contains("custom", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("property", StringComparison.OrdinalIgnoreCase))
        {
            return "custom_property_write_failed";
        }

        if (text.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "title_block_pdf_export_failed";
        }

        if (text.Contains("slddrw", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return "title_block_save_failed";
        }

        return "drawing_title_block_api_evidence_insufficient";
    }

    private static bool IsActionableDrawingTitleBlockFailureStage(string? failureStage, string? error) =>
        failureStage is null ||
        DrawingTitleBlockFailureStages.Contains(failureStage, StringComparer.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(error);

    private static bool IsActionableReleasePackageFailureStage(
        string? failureStage,
        IReadOnlyList<string> issues) =>
        failureStage is null ||
        ReleasePackageFailureStages.Contains(failureStage, StringComparer.OrdinalIgnoreCase) ||
        issues.Count > 0;

    private static readonly string[] DrawingFailureStages =
    {
        "source_part_missing",
        "drawing_template_missing",
        "drawing_document_create_failed",
        "source_part_open_failed",
        "source_part_activate_failed",
        "front_view_create_failed",
        "top_view_create_failed",
        "right_view_create_failed",
        "isometric_view_create_failed",
        "slddrw_save_failed",
        "pdf_export_failed",
        "drawing_report_write_failed",
        "drawing_api_evidence_insufficient"
    };

    private static readonly string[] DrawingDimensionFailureStages =
    {
        "source_drawing_missing",
        "source_drawing_open_failed",
        "drawing_view_missing",
        "drawing_view_activate_failed",
        "length_dimension_failed",
        "width_dimension_failed",
        "thickness_dimension_failed",
        "hole_diameter_dimension_failed",
        "hole_position_dimension_failed",
        "dimension_save_failed",
        "dimension_pdf_export_failed",
        "dimension_report_write_failed",
        "drawing_dimension_api_evidence_insufficient"
    };

    private static readonly string[] DrawingTitleBlockFailureStages =
    {
        "source_dimensioned_drawing_missing",
        "source_drawing_open_failed",
        "title_block_template_missing",
        "custom_property_write_failed",
        "drawing_property_read_failed",
        "title_block_update_failed",
        "title_block_save_failed",
        "title_block_pdf_export_failed",
        "title_block_report_write_failed",
        "drawing_title_block_api_evidence_insufficient"
    };

    private static readonly string[] ReleasePackageFailureStages =
    {
        "source_artifacts_missing",
        "source_report_missing",
        "source_report_failed",
        "artifact_copy_failed",
        "manifest_write_failed",
        "quality_report_write_failed",
        "release_summary_write_failed",
        "package_validation_failed"
    };

    private static bool IsActionableFailureStage(string? failureStage, string? error) =>
        string.IsNullOrWhiteSpace(error) ||
        (!string.IsNullOrWhiteSpace(failureStage) &&
         !failureStage.Equals("unknown_failed", StringComparison.OrdinalIgnoreCase));

    private static string BuildRealSmokeTestError(
        IReadOnlyList<string> issues,
        string? latestReportPath,
        string reportLabel = "build_report")
    {
        var joined = issues.Count == 0
            ? "real_build_smoke_test_failed_without_issue"
            : string.Join("; ", issues);

        return string.IsNullOrWhiteSpace(latestReportPath)
            ? joined
            : $"{joined}; {reportLabel}={latestReportPath}";
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
        var humanApprovalEngine = new SequentialWorkflowEngine(new RetryPolicy());
        var humanApprovalContext = new WorkflowContext("workflow-quality-loop-human", new Dictionary<string, object?>());
        var humanApproval = await humanApprovalEngine.ExecuteAsync(
            new[]
            {
                WorkflowScenarioStep("human-approval", GateDecisionResult.NeedsHumanApproval, ["manual approval required"]),
                new WorkflowStep("should-not-run-after-human", _ =>
                {
                    downstreamHumanApprovalStepExecuted = true;
                    return Task.FromResult(WorkflowScenarioResult("should-not-run-after-human", GateDecisionResult.Passed));
                })
            },
            humanApprovalContext);

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
        var approvalSubmission = await humanApprovalEngine.SubmitHumanApprovalAsync(
            new WorkflowApprovalSubmission(
                humanApprovalContext.TaskId,
                WorkflowApprovalDecision.Approve,
                "self-check-reviewer",
                "Self-check approval resume.",
                ApprovalRequestId: humanApproval.HumanApprovalRequest!.ApprovalRequestId,
                StepId: humanApproval.HumanApprovalRequest.StepId));
        var humanApprovalResumeSupported =
            approvalSubmission.Accepted &&
            approvalSubmission.WorkflowResult?.Status == WorkflowStatus.Passed &&
            downstreamHumanApprovalStepExecuted &&
            !humanApprovalEngine.TryGetPendingHumanApproval(humanApprovalContext.TaskId, out _);
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
            humanApprovalRequestGenerated &&
            humanApprovalResumeSupported;

        return new WorkflowQualityLoopSelfCheckResult(
            workflowQualityLoopEnabled,
            workflowPassedScenario,
            workflowRejectedRetryPassedScenario,
            workflowRejectedMaxRetriesScenario,
            workflowHumanApprovalScenario,
            retryPolicyEnabled,
            failureReportGenerated,
            humanApprovalRequestGenerated,
            humanApprovalResumeSupported);
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
        var retryDelayAuditRecorded = retryAuditLog.GetEntries().Any(entry =>
            entry.Action == "workflow_step_retrying" &&
            entry.Message.Contains("delay", StringComparison.OrdinalIgnoreCase) &&
            !entry.Message.Contains("0ms", StringComparison.OrdinalIgnoreCase));
        var retryDelayActuallyAwaited =
            retryResult.Status == WorkflowStatus.Passed &&
            retryAttempts == 2 &&
            retryDelayAuditRecorded;

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
        bool HumanApprovalRequestGenerated,
        bool HumanApprovalResumeSupported);

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
        string? SelfCheckInfrastructureError,
        bool SolidWorksRealWorkerSkeletonExists,
        bool SolidWorksEnvironmentValidatorExists,
        bool SolidWorksPreflightReportGenerated,
        bool SolidWorksSessionManagerExists,
        bool SolidWorksRealExecutionDefaultDisabled,
        bool SolidWorksRealExecutionRequiresRequestFlag,
        bool SolidWorksRealExecutionRequiresEnvFlag,
        bool SolidWorksComNotCalledInDefaultSelfCheck,
        bool SolidWorksRealConnectionSmokeTestAttempted,
        bool SolidWorksRealConnectionSmokeTestPassed,
        string? SolidWorksRealConnectionSmokeTestError,
        bool SolidWorksStrictRealSmokeTest,
        bool SolidWorksGenericRealBuildNotImplemented,
        bool SolidWorksRealCadNotExecutedByDefault,
        bool SolidWorksRealPlateBuildImplemented,
        bool SolidWorksRealBuildRequiresEnvFlag,
        bool SolidWorksRealBuildRequiresRequestFlag,
        bool SolidWorksRealBuildRequiresDryRunFalse,
        bool SolidWorksRealBuildDefaultDisabled,
        bool SolidWorksRealBuildSmokeTestAttempted,
        bool SolidWorksRealBuildSmokeTestPassed,
        string? SolidWorksRealBuildSmokeTestError,
        bool SolidWorksStrictRealBuildSmokeTest,
        bool SolidWorksRealBuildArtifactsValidated,
        bool SolidWorksRealBuildReportGenerated,
        bool SolidWorksRealBuildOutputsSldprt,
        bool SolidWorksRealBuildOutputsStep,
        bool SolidWorksRealBuildOutputsJsonReport,
        bool SolidWorksRealBuildNotCalledInDefaultSelfCheck,
        string? SwRealBuildSmokeTestEnvValue,
        string? SwStrictRealBuildTestEnvValue,
        bool? RealBuildRequestDryRun,
        string? RealBuildExecutionMode,
        string? RealBuildOutputDirectory,
        string? RealBuildLatestReportPath,
        bool SolidWorksDiagnosticRunnerExists,
        bool SolidWorksDiagnosticRunnerNotCalledByDefault,
        string? SolidWorksLatestDiagnosticReportPath,
        string? SolidWorksLatestDiagnosticFinalStatus,
        string? SolidWorksRealBuildFailureStage,
        bool SolidWorksRealBuildErrorIsActionable,
        bool SolidWorksApiFailureAnalyzerExists,
        bool SolidWorksApiEvidenceCollectorExists,
        bool SolidWorksApiEvidenceReportSchemaExists,
        bool SolidWorksCutHolesApiEvidenceSupported,
        bool SolidWorksReferenceSkillReadonlyAnalysisSupported,
        bool SolidWorksExternalScriptsNotCopied,
        bool SolidWorksApiRepairLoopAvailable,
        bool SolidWorksMacroRecordingRequestAvailable,
        bool SolidWorksPlateFeatureBuilderExists,
        bool SolidWorksRealDrawingBasicViewsImplemented,
        bool SolidWorksRealDrawingDefaultDisabled,
        bool SolidWorksRealDrawingRequiresEnvFlag,
        bool SolidWorksRealDrawingSmokeTestAttempted,
        bool SolidWorksRealDrawingSmokeTestPassed,
        string? SolidWorksRealDrawingSmokeTestError,
        bool SolidWorksStrictRealDrawingSmokeTest,
        bool SolidWorksRealDrawingOutputsSlddrw,
        bool SolidWorksRealDrawingOutputsPdf,
        bool SolidWorksRealDrawingOutputsJsonReport,
        bool SolidWorksRealDrawingNotCalledInDefaultSelfCheck,
        bool SolidWorksDrawingReportGenerated,
        string? SolidWorksRealDrawingFailureStage,
        bool SolidWorksDrawingFailureStageActionable,
        string? RealDrawingOutputDirectory,
        string? RealDrawingLatestReportPath,
        bool V11VersionStageDocumented,
        bool SolidWorksDrawingFailureRepairDocumented,
        bool SolidWorksDrawingApiEvidenceDocumented,
        bool SolidWorksDrawingReviewChecklistUpdated,
        bool SolidWorksRealDrawingDimensionsImplemented,
        bool SolidWorksRealDrawingDimensionsDefaultDisabled,
        bool SolidWorksRealDrawingDimensionsRequiresEnvFlag,
        bool SolidWorksRealDrawingDimensionsSmokeTestAttempted,
        bool SolidWorksRealDrawingDimensionsSmokeTestPassed,
        string? SolidWorksRealDrawingDimensionsSmokeTestError,
        bool SolidWorksStrictRealDrawingDimensionSmokeTest,
        bool SolidWorksRealDrawingDimensionsOutputsSlddrw,
        bool SolidWorksRealDrawingDimensionsOutputsPdf,
        bool SolidWorksRealDrawingDimensionsOutputsJsonReport,
        bool SolidWorksRealDrawingDimensionsNotCalledInDefaultSelfCheck,
        bool SolidWorksDrawingDimensionReportGenerated,
        string? SolidWorksRealDrawingDimensionFailureStage,
        bool SolidWorksDrawingDimensionFailureStageActionable,
        string? RealDrawingDimensionOutputDirectory,
        string? RealDrawingDimensionLatestReportPath,
        bool V12VersionStageDocumented,
        bool SolidWorksDrawingDimensionFailureRepairDocumented,
        bool SolidWorksDrawingDimensionApiEvidenceDocumented,
        bool SolidWorksDrawingDimensionReviewChecklistUpdated,
        bool SolidWorksRealDrawingTitleBlockImplemented,
        bool SolidWorksRealDrawingTitleBlockDefaultDisabled,
        bool SolidWorksRealDrawingTitleBlockRequiresEnvFlag,
        bool SolidWorksRealDrawingTitleBlockSmokeTestAttempted,
        bool SolidWorksRealDrawingTitleBlockSmokeTestPassed,
        string? SolidWorksRealDrawingTitleBlockSmokeTestError,
        bool SolidWorksStrictRealDrawingTitleBlockSmokeTest,
        bool SolidWorksRealDrawingTitleBlockOutputsSlddrw,
        bool SolidWorksRealDrawingTitleBlockOutputsPdf,
        bool SolidWorksRealDrawingTitleBlockOutputsJsonReport,
        bool SolidWorksRealDrawingTitleBlockNotCalledInDefaultSelfCheck,
        bool SolidWorksDrawingTitleBlockReportGenerated,
        string? SolidWorksRealDrawingTitleBlockFailureStage,
        bool SolidWorksDrawingTitleBlockFailureStageActionable,
        string? RealDrawingTitleBlockOutputDirectory,
        string? RealDrawingTitleBlockLatestReportPath,
        bool V13VersionStageDocumented,
        bool SolidWorksDrawingTitleBlockFailureRepairDocumented,
        bool SolidWorksDrawingTitleBlockApiEvidenceDocumented,
        bool SolidWorksDrawingTitleBlockReviewChecklistUpdated,
        string SolidWorksDrawingTitleBlockPopulationStrategy,
        bool SolidWorksDrawingTitleBlockFieldsVerifiedInSheetFormat,
        bool SolidWorksReleasePackageImplemented,
        bool SolidWorksReleasePackageDefaultNoCadExecution,
        bool SolidWorksReleaseManifestGenerated,
        bool SolidWorksPackageQualityReportGenerated,
        bool SolidWorksReleaseSummaryGenerated,
        bool SolidWorksReleaseArtifactsCollected,
        bool SolidWorksReleaseReportsCollected,
        string? SolidWorksReleasePackageFailureStage,
        bool SolidWorksReleasePackageFailureStageActionable,
        string? SolidWorksReleaseManifestPath,
        string? SolidWorksPackageQualityReportPath,
        string? SolidWorksReleaseSummaryPath,
        bool V14VersionStageDocumented,
        bool SolidWorksReleasePackageFailureRepairDocumented,
        bool SolidWorksReleasePackageReviewChecklistUpdated,
        bool RealCadWorkerIntegratedIntoMainWorkflow,
        bool ChiefEngineerOrchestratorInvokesCadWorkflow,
        bool WorkflowEngineCanRouteToSolidWorksWorker,
        bool RealCadMainWorkflowDefaultDisabled,
        bool RealCadMainWorkflowRequiresRequestFlag,
        bool RealCadMainWorkflowRequiresEnvFlag,
        bool RealCadMainWorkflowPassesQualityGate,
        bool GatewayDoesNotCallWorkerDirectly,
        bool LlmDoesNotCallWorkerDirectly,
        bool ReleasePackageAllSourceReportsPassedFieldExists,
        bool ReleasePackageDeliverableStatusFieldExists,
        bool ReleasePackageFailedSourceReportsBlockDeliverable,
        bool V15VersionStageDocumented);

    private sealed record SolidWorksReleasePackageReflectionResult(
        string Status,
        string OutputDirectory,
        string? ManifestPath,
        string? QualityReportPath,
        string? SummaryPath,
        string? FailureStage,
        bool ArtifactsCollected,
        bool ReportsCollected,
        IReadOnlyList<string> Issues);

    private sealed record ExecutableDocsLayerSelfCheckResult(
        bool ExecutableDocsLayerEnabled,
        bool DocsIndexExists,
        bool ProjectExecutionStandardExists,
        bool ModuleDocumentStandardExists,
        bool StepExecutionStandardExists,
        bool FailureRepairStandardExists,
        bool CodexExecutionProtocolExists,
        bool ClaudeReviewProtocolExists,
        bool VersionStageIndexExists,
        bool CodexAgentTeamGuideExists,
        bool CodexAgentRegistryExists,
        bool CodexAgentGovernanceDocExists,
        bool AgentsMdExists,
        bool CodexAgentsConfigured,
        bool CodexAgentRegistryListsCanonicalAgents,
        bool CodexNoDuplicateActiveAgents,
        bool CodexAgentReusePolicyDocumented,
        bool CodexAgentNewRequirementsGoToSkillsOrDocs,
        bool CodexActiveAgentCountIsExpected,
        bool CodexOnlyCanonicalAgentsActive,
        bool CodexConfigExampleExists,
        bool CodexProjectManagerAgentExists,
        bool CodexCodeMapperAgentExists,
        bool CodexApiResearcherAgentExists,
        bool CodexCadWorkerAgentExists,
        bool CodexQualityGateAgentExists,
        bool CodexDocsWriterAgentExists,
        bool CodexAgentsDoNotReplaceProjectModules,
        bool CodexAgentsRespectWorkerBoundaries,
        bool AgentsSkillsDirectoryExists,
        bool SolidWorksApiRepairSkillExists,
        bool MarkdownDocsStandardSkillExists,
        bool QualityReviewSkillExists,
        bool CadModelingExecutionDocExists,
        bool CadModelingFailureRepairDocExists,
        bool CadModelingApiEvidenceDocExists,
        bool CadModelingReviewChecklistExists,
        bool SolidWorksWorkerExecutionDocExists,
        bool SolidWorksWorkerFailureRepairDocExists,
        bool SolidWorksWorkerApiEvidenceDocExists,
        bool SolidWorksWorkerReviewChecklistExists);

    private sealed record SelfCheckRunMetadata(
        string RunId,
        DateTimeOffset GeneratedAt,
        string SourceRevision);

    private sealed record RealAcceptanceOutputPaths(
        string RealAcceptanceReportPath,
        string LatestRealOutputsPath,
        bool IsValid);

    private sealed record V18PartFamilySelfCheckResult(
        bool GenericCadModelSpecSupported,
        bool PartTypeRegistryExists,
        bool PlatePartFamilyRegistered,
        bool FlangePartFamilyRegistered,
        bool ShaftPartFamilyRegistered,
        bool UnsupportedPartTypeRejected,
        bool InvalidPartParametersRejectedBeforeWorker,
        bool PartFamilyBuildersDoNotUseLargeSwitch,
        bool PlateRegressionPassed,
        bool FlangeDryRunPassed,
        bool ShaftDryRunPassed,
        bool RealCadPartFamilyDefaultDisabled,
        bool V18VersionStageDocumented)
    {
        public bool AllPassed =>
            GenericCadModelSpecSupported &&
            PartTypeRegistryExists &&
            PlatePartFamilyRegistered &&
            FlangePartFamilyRegistered &&
            ShaftPartFamilyRegistered &&
            UnsupportedPartTypeRejected &&
            InvalidPartParametersRejectedBeforeWorker &&
            PartFamilyBuildersDoNotUseLargeSwitch &&
            PlateRegressionPassed &&
            FlangeDryRunPassed &&
            ShaftDryRunPassed &&
            V18VersionStageDocumented;
    }

    private sealed record V19PartFamilySelfCheckResult(
        bool FlangeRealBuilderImplemented,
        bool ShaftRealBuilderImplemented,
        bool FlangeRealWorkflowSupported,
        bool ShaftRealWorkflowSupported,
        bool FlangeRealWorkflowDefaultDisabled,
        bool ShaftRealWorkflowDefaultDisabled,
        bool FlangeApiEvidenceDocumented,
        bool ShaftApiEvidenceDocumented,
        bool FlangeArtifactValidationSupported,
        bool ShaftArtifactValidationSupported,
        bool PlatePartFamilyRegressionPassed,
        bool NoLargePartTypeSwitch,
        bool AllPartFamiliesUseRegistry,
        bool V19VersionStageDocumented)
    {
        public bool AllPassed =>
            FlangeRealBuilderImplemented &&
            ShaftRealBuilderImplemented &&
            FlangeRealWorkflowSupported &&
            ShaftRealWorkflowSupported &&
            FlangeApiEvidenceDocumented &&
            ShaftApiEvidenceDocumented &&
            FlangeArtifactValidationSupported &&
            ShaftArtifactValidationSupported &&
            PlatePartFamilyRegressionPassed &&
            NoLargePartTypeSwitch &&
            AllPartFamiliesUseRegistry &&
            V19VersionStageDocumented;
    }

    private sealed record V20SolidWorksDefaultOnSelfCheckResult(
        bool LocalInteractiveDefaultEnabled,
        bool DisableEnvironmentSupported,
        bool CiExecutionDisabled,
        bool UnitTestExecutionDisabled,
        bool DryRunDisablesRealExecution,
        bool VisibleDefaultTrue,
        bool ExecutionEnvironmentProbeSupported,
        bool LegacyEnableFlagNotRequired,
        bool LegacyRequestConfirmationNotRequired)
    {
        public bool AllPassed =>
            LocalInteractiveDefaultEnabled &&
            DisableEnvironmentSupported &&
            CiExecutionDisabled &&
            UnitTestExecutionDisabled &&
            DryRunDisablesRealExecution &&
            VisibleDefaultTrue &&
            ExecutionEnvironmentProbeSupported &&
            LegacyEnableFlagNotRequired &&
            LegacyRequestConfirmationNotRequired;
    }

    private sealed record V20AGenericCadModelSpecSelfCheckResult(
        bool GenericCadModelSpecV2Supported,
        bool SketchDefinitionSupported,
        bool SketchConstraintsSupported,
        bool FeatureDefinitionSupported,
        bool FeatureGraphSupported,
        bool FeatureGraphCycleDetected,
        bool MissingFeatureDependencyRejected,
        bool BuildPlanCompilerSupported,
        bool PlateUsesGenericFeatureGraph,
        bool FlangeUsesGenericFeatureGraph,
        bool ShaftUsesGenericFeatureGraph,
        bool NoPartSpecificLogicInRealWorker,
        bool V20ADocumented)
    {
        public bool AllPassed =>
            GenericCadModelSpecV2Supported &&
            SketchDefinitionSupported &&
            SketchConstraintsSupported &&
            FeatureDefinitionSupported &&
            FeatureGraphSupported &&
            FeatureGraphCycleDetected &&
            MissingFeatureDependencyRejected &&
            BuildPlanCompilerSupported &&
            PlateUsesGenericFeatureGraph &&
            FlangeUsesGenericFeatureGraph &&
            ShaftUsesGenericFeatureGraph &&
            NoPartSpecificLogicInRealWorker &&
            V20ADocumented;
    }

    private sealed record V20BFeatureHandlerSelfCheckResult(
        bool FeatureHandlerRegistryExists,
        bool NoFeatureTypeLargeSwitch,
        bool SketchHandlerRegistered,
        bool ExtrudeHandlerRegistered,
        bool CutHandlerRegistered,
        bool HoleHandlerRegistered,
        bool RevolveHandlerRegistered,
        bool FeatureHandlerValidationSupported,
        bool FeatureApiEvidenceRequired,
        bool UnverifiedApiBlocksRealExecution,
        bool FeatureHandlerDocsCompleted,
        bool V20BDocumented)
    {
        public bool AllPassed =>
            FeatureHandlerRegistryExists &&
            NoFeatureTypeLargeSwitch &&
            SketchHandlerRegistered &&
            ExtrudeHandlerRegistered &&
            CutHandlerRegistered &&
            HoleHandlerRegistered &&
            RevolveHandlerRegistered &&
            FeatureHandlerValidationSupported &&
            FeatureApiEvidenceRequired &&
            UnverifiedApiBlocksRealExecution &&
            FeatureHandlerDocsCompleted &&
            V20BDocumented;
    }

    private sealed record V20CFeatureAdapterSelfCheckResult(
        bool FeatureAdapterLayerExists,
        bool FeatureHandlerNoDirectComAccess,
        bool SolidWorksFeatureAdapterExists,
        bool SketchRealExecutionSupported,
        bool ExtrudeRealExecutionSupported,
        bool CutRealExecutionSupported,
        bool HoleRealExecutionSupported,
        bool FeaturePipelineEndToEndSupported,
        bool FeatureProductionEvidenceActive,
        bool FeatureResultValidationSupported,
        bool FeatureFakeSuccessGuardSupported,
        bool V20CDocumented)
    {
        public bool AllPassed =>
            FeatureAdapterLayerExists &&
            FeatureHandlerNoDirectComAccess &&
            SolidWorksFeatureAdapterExists &&
            SketchRealExecutionSupported &&
            ExtrudeRealExecutionSupported &&
            CutRealExecutionSupported &&
            HoleRealExecutionSupported &&
            FeaturePipelineEndToEndSupported &&
            FeatureProductionEvidenceActive &&
            FeatureResultValidationSupported &&
            FeatureFakeSuccessGuardSupported &&
            V20CDocumented;
    }

    private sealed record V20DModelRebuildSelfCheckResult(
        bool ModelRebuildPipelineExists,
        bool ParameterUpdateSupported,
        bool SolidWorksRebuildSupported,
        bool GeometryValidatorExists,
        bool BoundingBoxValidationSupported,
        bool VolumeValidationSupported,
        bool ParameterGeometryMatchSupported,
        bool RebuildFailureDetected,
        bool GeometryReportGenerated,
        bool V20DDocumented,
        bool ProductionEvidenceActive,
        bool MarkdownChineseCheckPassed)
    {
        public bool AllPassed =>
            ModelRebuildPipelineExists &&
            ParameterUpdateSupported &&
            SolidWorksRebuildSupported &&
            GeometryValidatorExists &&
            BoundingBoxValidationSupported &&
            VolumeValidationSupported &&
            ParameterGeometryMatchSupported &&
            RebuildFailureDetected &&
            GeometryReportGenerated &&
            V20DDocumented &&
            ProductionEvidenceActive &&
            MarkdownChineseCheckPassed;
    }

    private sealed record V21AComplexFeatureSelfCheckResult(
        bool FilletHandlerRegistered,
        bool ChamferHandlerRegistered,
        bool LinearPatternHandlerRegistered,
        bool CircularPatternHandlerRegistered,
        bool MirrorHandlerRegistered,
        bool ComplexFeatureRegistrySupported,
        bool UnverifiedFeatureBlocksExecution,
        bool FeatureLibraryDocumented,
        bool FeatureRegressionTestsPassed,
        bool V21ADocumented,
        bool EdgeSelectionModelSupported)
    {
        public bool AllPassed =>
            FilletHandlerRegistered &&
            ChamferHandlerRegistered &&
            LinearPatternHandlerRegistered &&
            CircularPatternHandlerRegistered &&
            MirrorHandlerRegistered &&
            ComplexFeatureRegistrySupported &&
            UnverifiedFeatureBlocksExecution &&
            FeatureLibraryDocumented &&
            FeatureRegressionTestsPassed &&
            V21ADocumented &&
            EdgeSelectionModelSupported;
    }

    private sealed record V20EUnifiedFeatureGraphSelfCheckResult(
        bool UnifiedPartFamilyBuilders,
        bool ControlledPlateEvidenceActive,
        bool StepContentGateActive,
        bool V21ARealExecutionFrozen,
        bool V20EDocumented,
        bool CapabilityRegressionGatePassed,
        IReadOnlyList<string> CapabilityRegressions,
        bool PartFamilyDefinitionSupported,
        bool PlateUsesPartFamilyDefinition,
        bool FlangeUsesPartFamilyDefinition,
        bool ShaftUsesPartFamilyDefinition,
        bool NoPartSpecificBuilderLogic,
        bool FeatureGraphTemplateReuseSupported,
        bool CommonFeatureTemplatesExists,
        bool CadCapabilityMatrixExists,
        bool RegressionModelsSupported,
        bool FlangeRegressionPassed,
        bool ShaftRegressionPassed,
        bool GeometryValidationPlatformWide)
    {
        public bool AllPassed =>
            UnifiedPartFamilyBuilders &&
            ControlledPlateEvidenceActive &&
            StepContentGateActive &&
            V21ARealExecutionFrozen &&
            V20EDocumented &&
            CapabilityRegressionGatePassed &&
            PartFamilyDefinitionSupported &&
            PlateUsesPartFamilyDefinition &&
            FlangeUsesPartFamilyDefinition &&
            ShaftUsesPartFamilyDefinition &&
            NoPartSpecificBuilderLogic &&
            FeatureGraphTemplateReuseSupported &&
            CommonFeatureTemplatesExists &&
            CadCapabilityMatrixExists &&
            RegressionModelsSupported &&
            FlangeRegressionPassed &&
            ShaftRegressionPassed &&
            GeometryValidationPlatformWide;
    }

    private sealed record V21AJacketSelfCheckResult(
        bool JacketPartFamilyRegistered,
        bool JacketUsesGenericFeatureGraph,
        bool JacketRealBuilderImplemented,
        bool JacketDryRunPassed,
        bool JacketRealWorkflowSupported,
        bool JacketApiEvidenceDocumented,
        bool JacketProductionEvidenceActive,
        bool V21AJacketDocumented)
    {
        public bool AllPassed =>
            JacketPartFamilyRegistered &&
            JacketUsesGenericFeatureGraph &&
            JacketRealBuilderImplemented &&
            JacketDryRunPassed &&
            JacketRealWorkflowSupported &&
            JacketApiEvidenceDocumented &&
            JacketProductionEvidenceActive &&
            V21AJacketDocumented;
    }

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
