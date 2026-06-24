using System.Text.Json;
using System.Text.Json.Serialization;
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
