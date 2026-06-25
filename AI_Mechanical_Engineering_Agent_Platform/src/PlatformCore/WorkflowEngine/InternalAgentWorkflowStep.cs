using AgentContracts;
using DomainSchemas;
using QualityGate;

namespace PlatformCore;

public sealed class InternalAgentWorkflowStep
{
    private readonly InternalAgentRouter _router;
    private readonly AgentRegistry _agentRegistry;
    private readonly InMemoryAuditLog _auditLog;
    private readonly IGatekeeper _gatekeeper;
    private int _attemptCount;

    public InternalAgentWorkflowStep(
        string agentId,
        AgentContext inputContext,
        InternalAgentRouter router,
        AgentRegistry agentRegistry,
        InMemoryAuditLog auditLog,
        int maxRetries = 2,
        IGatekeeper? gatekeeper = null)
    {
        AgentId = agentId;
        InputContext = inputContext;
        _router = router;
        _agentRegistry = agentRegistry;
        _auditLog = auditLog;
        _gatekeeper = gatekeeper ?? new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder());
        MaxRetries = maxRetries;

        var agent = ResolveAgent();
        AgentRole = agent.Role;
        StepId = $"internal-agent:{agent.Id}";
        StepName = $"Internal Agent: {agent.Name}";
    }

    public string StepId { get; }

    public string StepName { get; }

    public string AgentId { get; }

    public AgentRole AgentRole { get; }

    public AgentContext InputContext { get; }

    public AgentOutput? AgentOutput { get; private set; }

    public ReviewReport? ReviewReport { get; private set; }

    public GateDecision? GateDecision { get; private set; }

    public int RetryCount => Math.Max(0, _attemptCount - 1);

    public int MaxRetries { get; }

    public WorkflowStepStatus Status { get; private set; } = WorkflowStepStatus.Pending;

    public IReadOnlyList<string> Issues { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<string> Logs { get; private set; } = Array.Empty<string>();

    public WorkflowStep ToWorkflowStep() =>
        new(StepName, ExecuteAsync, StepId, MaxRetries: MaxRetries);

    public async Task<WorkflowStepResult> ExecuteAsync(WorkflowContext workflowContext)
    {
        _attemptCount++;
        Status = WorkflowStepStatus.Running;

        var contextWithAttempt = WithAttemptContext(workflowContext);
        var output = await _router.InvokeInternalAgentAsync(AgentId, contextWithAttempt);
        var review = AgentOutputReviewMapper.ToReviewReport($"internal-agent-step-{AgentId}", output);
        var gateEvaluation = _gatekeeper.Evaluate(review);
        var status = ToStepStatus(gateEvaluation.Decision.Result);
        var issues = output.Issues
            .Concat(review.Issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var logs = output.Logs
            .Concat(new[] { $"QualityGate evaluated internal agent '{AgentId}' as {gateEvaluation.Decision.Result}." })
            .ToArray();

        AgentOutput = output;
        ReviewReport = review;
        GateDecision = gateEvaluation.Decision;
        Status = status;
        Issues = issues;
        Logs = logs;

        _auditLog.Record(
            "quality-gate",
            AgentId,
            "quality_gate_after_internal_step",
            $"QualityGate evaluated internal agent step '{AgentId}' with decision {gateEvaluation.Decision.Result}.");

        return new WorkflowStepResult(
            StepId,
            StepName,
            status,
            output.Message,
            output,
            review,
            gateEvaluation.Decision,
            gateEvaluation.RejectReport,
            RetryCount,
            MaxRetries,
            Logs: logs,
            Issues: issues);
    }

    private AgentContext WithAttemptContext(WorkflowContext workflowContext)
    {
        var sharedState = InputContext.SharedState.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        sharedState["internal_agent_attempt"] = _attemptCount;
        sharedState["internal_workflow_id"] = workflowContext.TaskId;
        sharedState["internal_workflow_step_id"] = StepId;

        return InputContext with { SharedState = sharedState };
    }

    private AgentContracts.IAgent ResolveAgent()
    {
        var agent = _agentRegistry.GetById(AgentId)
            ?? throw new InvalidOperationException($"Internal agent '{AgentId}' was not found.");
        if (agent.Visibility != AgentVisibility.Internal)
        {
            throw new InvalidOperationException($"Agent '{AgentId}' is not an Internal agent.");
        }

        return agent;
    }

    private static WorkflowStepStatus ToStepStatus(GateDecisionResult result) =>
        result switch
        {
            GateDecisionResult.Passed => WorkflowStepStatus.Passed,
            GateDecisionResult.Rejected => WorkflowStepStatus.Rejected,
            GateDecisionResult.Failed => WorkflowStepStatus.Failed,
            GateDecisionResult.NeedsHumanApproval => WorkflowStepStatus.WaitingForHumanApproval,
            _ => WorkflowStepStatus.Running
        };
}
