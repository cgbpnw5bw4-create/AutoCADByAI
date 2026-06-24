using AgentContracts;
using DomainSchemas;
using PlatformCore;

namespace PlatformCore.Modules.RequirementUnderstanding.Agents;

public sealed class ChiefEngineerOrchestrator
{
    private static readonly string[] InternalRoute =
    [
        "mechanical-designer",
        "cad-modeler",
        "drawing-engineer",
        "drawing-reviewer"
    ];

    private readonly InternalAgentRouter _router;
    private readonly AgentRegistry _agentRegistry;
    private readonly InMemoryAuditLog _auditLog;

    public ChiefEngineerOrchestrator(
        InternalAgentRouter router,
        AgentRegistry agentRegistry,
        InMemoryAuditLog auditLog)
    {
        _router = router;
        _agentRegistry = agentRegistry;
        _auditLog = auditLog;
    }

    public async Task<AgentOutput> ExecuteAsync(AgentContext context, string rootAgentId, string rootAgentName)
    {
        var outputs = await _router.InvokeInternalAgentsSequentiallyAsync(InternalRoute, context);
        var report = BuildReport(context, rootAgentId, outputs);
        _auditLog.Record("agent", rootAgentId, "collaboration_report_created", $"Internal collaboration report created for {context.Input.ConversationId}.");

        var artifact = new ArtifactInfo(
            $"collaboration-{Guid.NewGuid():N}",
            "internal-collaboration-report",
            "internal-collaboration-report",
            $"memory://internal-collaboration/{context.Input.ConversationId}",
            "application/json");

        var issues = report.Issues;
        var status = outputs.Any(output => output.Status == AgentOutputStatus.Failed)
            ? AgentOutputStatus.Failed
            : issues.Count > 0 ? AgentOutputStatus.Rejected : AgentOutputStatus.Completed;

        return new AgentOutput(
            status,
            $"{rootAgentName} completed internal multi-agent routing.",
            new[] { artifact }.Concat(report.Artifacts).ToArray(),
            issues,
            new[] { "Chief engineer orchestrated internal agents through InternalAgentRouter." },
            report.CalledAgents.LastOrDefault()?.NextRecommendedAgentId,
            report,
            report.AgentOutputs.LastOrDefault(output => output.ReviewReport is not null)?.ReviewReport);
    }

    private InternalCollaborationReport BuildReport(
        AgentContext context,
        string rootAgentId,
        IReadOnlyList<AgentOutput> outputs)
    {
        var calledAgents = new List<CalledAgentSummary>();
        var snapshots = new List<AgentOutputSnapshot>();
        var artifacts = new List<ArtifactInfo>();
        var issues = new List<string>();

        for (var index = 0; index < InternalRoute.Length; index++)
        {
            var agentId = InternalRoute[index];
            var agent = _agentRegistry.GetById(agentId)
                ?? throw new InvalidOperationException($"Internal agent '{agentId}' was not found while building collaboration report.");
            var output = outputs[index];

            calledAgents.Add(new CalledAgentSummary(
                agent.Id,
                agent.Name,
                agent.Role.Description,
                output.Status.ToString(),
                output.Message,
                output.NextRecommendedAgentId));

            snapshots.Add(new AgentOutputSnapshot(
                agent.Id,
                output.Status.ToString(),
                output.Message,
                output.Artifacts,
                output.Issues,
                output.NextRecommendedAgentId,
                output.ReviewReport));

            artifacts.AddRange(output.Artifacts);
            issues.AddRange(output.Issues);
        }

        var failedAgents = snapshots
            .Where(snapshot => string.Equals(snapshot.Status, AgentOutputStatus.Failed.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(snapshot => snapshot.AgentId)
            .ToArray();

        var summary = failedAgents.Length == 0
            ? "Internal agents completed the V0.2 mechanical engineering planning route."
            : $"Internal route failed at: {string.Join(", ", failedAgents)}.";

        var recommendation = failedAgents.Length == 0
            ? "Proceed to QualityGate; do not call CAD workers in V0.2."
            : "Route to error-diagnosis before any downstream work.";

        return new InternalCollaborationReport(
            context.Input.ConversationId,
            rootAgentId,
            calledAgents,
            snapshots,
            issues,
            artifacts,
            summary,
            recommendation);
    }
}
