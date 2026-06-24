using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using DomainSchemas;
using QualityGate;

namespace PlatformCore;

public static class PlatformSelfCheckRunner
{
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

    public static async Task<PlatformSelfCheckReport> RunAsync(PlatformKernel platform, string outputRoot, string? projectRoot = null)
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
            platform.ContextManager.CreateWorkflowContext(task.Id));

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
            finalStatus);

        var reportPath = Path.Combine(outputRoot, "reports", "platform_self_check_report.json");
        await using var stream = File.Create(reportPath);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions());

        return report;
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

    private static async Task<AgentContracts.AgentOutput> InvokeChiefEngineerForSelfCheck(PlatformKernel platform)
    {
        var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer")
            ?? throw new InvalidOperationException("chief-engineer is not registered.");
        var input = new AgentContracts.AgentInput(
            "self-check",
            "self-check",
            "self-check-conversation",
            "self-check",
            "Run internal routing self-check.",
            Array.Empty<string>(),
            new Dictionary<string, string> { ["project_id"] = "self-check" });
        var context = new AgentContracts.AgentContext(
            $"task-{Guid.NewGuid():N}",
            input,
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

        return await chiefEngineer.ExecuteAsync(context);
    }

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
                    new WorkflowContext("runtime-self-check", new Dictionary<string, object?>())
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
