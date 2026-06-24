using System.Text.Json;
using System.Text.Json.Serialization;
using DomainSchemas;

namespace PlatformCore;

public static class PlatformSelfCheckRunner
{
    public static async Task<PlatformSelfCheckReport> RunAsync(PlatformKernel platform, string outputRoot)
    {
        Directory.CreateDirectory(Path.Combine(outputRoot, "reports"));

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
                Step("quality-gate", "QualityGate skeleton returned Passed.", gateDecision)
            },
            platform.ContextManager.CreateWorkflowContext(task.Id));

        var checksPassed =
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
