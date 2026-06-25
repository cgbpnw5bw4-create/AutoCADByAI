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
    private readonly SequentialWorkflowEngine _workflowEngine;

    public ChiefEngineerOrchestrator(
        InternalAgentRouter router,
        AgentRegistry agentRegistry,
        InMemoryAuditLog auditLog,
        SequentialWorkflowEngine? workflowEngine = null)
    {
        _router = router;
        _agentRegistry = agentRegistry;
        _auditLog = auditLog;
        _workflowEngine = workflowEngine ?? new SequentialWorkflowEngine(SequentialWorkflowEngine.CreateDefaultRetryPolicy(), auditLog);
    }

    public async Task<AgentOutput> ExecuteAsync(AgentContext context, string rootAgentId, string rootAgentName)
    {
        var workflowId = $"internal-collaboration-{context.TaskId}";
        var workflowSteps = InternalRoute
            .Select(agentId => new InternalAgentWorkflowStep(agentId, context, _router, _agentRegistry, _auditLog).ToWorkflowStep())
            .ToArray();
        var workflowResult = await _workflowEngine.ExecuteAsync(
            workflowSteps,
            new WorkflowContext(workflowId, new Dictionary<string, object?>
            {
                ["root_agent_id"] = rootAgentId,
                ["conversation_id"] = context.Input.ConversationId
            }));

        var report = BuildReport(context, rootAgentId, workflowResult);
        _auditLog.Record("agent", rootAgentId, "collaboration_report_created", $"Internal workflow-backed collaboration report created for {context.Input.ConversationId}.");

        var artifact = new ArtifactInfo(
            $"collaboration-{Guid.NewGuid():N}",
            "internal-collaboration-report",
            "internal-collaboration-report",
            $"memory://internal-collaboration/{context.Input.ConversationId}",
            "application/json");

        var status = workflowResult.Status switch
        {
            WorkflowStatus.Passed => AgentOutputStatus.Completed,
            WorkflowStatus.WaitingForHumanApproval => AgentOutputStatus.NeedsHumanApproval,
            WorkflowStatus.Failed => AgentOutputStatus.Failed,
            WorkflowStatus.Rejected => AgentOutputStatus.Rejected,
            _ => AgentOutputStatus.Failed
        };

        return new AgentOutput(
            status,
            $"{rootAgentName} completed workflow-backed internal multi-agent routing with status {workflowResult.Status}.",
            new[] { artifact }.Concat(report.Artifacts).ToArray(),
            report.Issues,
            new[] { "Chief engineer orchestrated internal agents through SequentialWorkflowEngine and QualityGate." },
            report.CalledAgents.LastOrDefault()?.NextRecommendedAgentId,
            report,
            ResolveFinalReviewReport(workflowResult));
    }

    private InternalCollaborationReport BuildReport(
        AgentContext context,
        string rootAgentId,
        WorkflowExecutionResult workflowResult)
    {
        var finalStepByAgent = workflowResult.Steps
            .Where(step => step.AgentOutput is not null)
            .GroupBy(step => AgentIdFromStepId(step.StepId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var calledAgents = new List<CalledAgentSummary>();
        var snapshots = new List<AgentOutputSnapshot>();
        var artifacts = new List<ArtifactInfo>();
        var issues = new List<string>();

        foreach (var agentId in InternalRoute.Where(finalStepByAgent.ContainsKey))
        {
            var agent = _agentRegistry.GetById(agentId)
                ?? throw new InvalidOperationException($"Internal agent '{agentId}' was not found while building collaboration report.");
            var step = finalStepByAgent[agentId];
            var output = step.AgentOutput!;

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
                output.ReviewReport ?? step.ReviewReport));

            artifacts.AddRange(output.Artifacts);
            issues.AddRange(step.Issues);
        }

        if (workflowResult.FailureReport is not null)
        {
            issues.Add(workflowResult.FailureReport.FailureReason);
        }

        if (workflowResult.HumanApprovalRequest is not null)
        {
            issues.Add(workflowResult.HumanApprovalRequest.Reason);
        }

        var stepResults = workflowResult.Steps
            .Select(step => new InternalWorkflowStepSummary(
                step.StepId,
                step.StepName,
                AgentIdFromStepId(step.StepId),
                step.Status.ToString(),
                step.GateDecision,
                step.RetryCount,
                step.MaxRetries,
                step.Issues,
                step.Logs))
            .ToArray();
        var retryStepIds = workflowResult.Steps
            .Where(step => step.Status == WorkflowStepStatus.Retrying)
            .Select(step => step.StepId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = workflowResult.Status switch
        {
            WorkflowStatus.Passed => "Internal agents completed the workflow-backed mechanical engineering planning route.",
            WorkflowStatus.WaitingForHumanApproval => $"Internal workflow is waiting for human approval at {workflowResult.HumanApprovalRequest?.StepId}.",
            WorkflowStatus.Failed => $"Internal workflow failed at {workflowResult.FailureReport?.FailedStepId}.",
            WorkflowStatus.Rejected => $"Internal workflow was rejected at {workflowResult.Steps.LastOrDefault()?.StepId}.",
            _ => $"Internal workflow ended with status {workflowResult.Status}."
        };
        var recommendation = workflowResult.Status switch
        {
            WorkflowStatus.Passed => "Proceed to Gateway QualityGate; do not call CAD workers in V0.6.",
            WorkflowStatus.WaitingForHumanApproval => "Pause automatic execution until human approval is recorded.",
            WorkflowStatus.Failed => "Route to error-diagnosis before any downstream work.",
            WorkflowStatus.Rejected => "Review RejectReport and retry policy before continuing.",
            _ => "Inspect internal workflow result."
        };

        return new InternalCollaborationReport(
            context.Input.ConversationId,
            rootAgentId,
            calledAgents,
            snapshots,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            artifacts,
            summary,
            recommendation,
            workflowResult.WorkflowId,
            workflowResult.Status.ToString(),
            stepResults,
            workflowResult.FinalGateDecision,
            new RetrySummary(retryStepIds.Length, retryStepIds),
            workflowResult.FailureReport,
            workflowResult.HumanApprovalRequest);
    }

    private static ReviewReport? ResolveFinalReviewReport(WorkflowExecutionResult workflowResult) =>
        workflowResult.Steps.LastOrDefault(step => step.ReviewReport is not null)?.ReviewReport;

    private static string AgentIdFromStepId(string stepId) =>
        stepId.StartsWith("internal-agent:", StringComparison.OrdinalIgnoreCase)
            ? stepId["internal-agent:".Length..]
            : stepId;
}
