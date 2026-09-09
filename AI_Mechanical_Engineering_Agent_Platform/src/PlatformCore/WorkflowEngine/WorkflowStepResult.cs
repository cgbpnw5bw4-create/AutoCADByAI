using AgentContracts;
using DomainSchemas;

namespace PlatformCore;

public sealed record WorkflowStepResult
{
    public WorkflowStepResult(
        string StepId,
        string StepName,
        WorkflowStepStatus Status,
        string Message,
        AgentOutput? AgentOutput = null,
        ReviewReport? ReviewReport = null,
        GateDecision? GateDecision = null,
        RejectReport? RejectReport = null,
        int RetryCount = 0,
        int MaxRetries = 0,
        string? NextStepId = null,
        IReadOnlyList<string>? Logs = null,
        IReadOnlyList<string>? Issues = null)
    {
        this.StepId = StepId;
        this.StepName = StepName;
        this.Status = Status;
        this.Message = Message;
        this.AgentOutput = AgentOutput;
        this.ReviewReport = ReviewReport;
        this.GateDecision = GateDecision;
        this.RejectReport = RejectReport;
        this.RetryCount = RetryCount;
        this.MaxRetries = MaxRetries;
        this.NextStepId = NextStepId;
        this.Logs = Logs ?? Array.Empty<string>();
        this.Issues = Issues ?? Array.Empty<string>();
    }

    public WorkflowStepResult(
        string StepName,
        string Status,
        string Message,
        GateDecision? GateDecision = null,
        IReadOnlyList<string>? Logs = null)
        : this(
            StepName,
            StepName,
            ParseStatus(Status, GateDecision),
            Message,
            GateDecision: GateDecision,
            Logs: Logs,
            Issues: Array.Empty<string>())
    {
    }

    public string StepId { get; init; }

    public string StepName { get; init; }

    public WorkflowStepStatus Status { get; init; }

    public string Message { get; init; }

    public AgentOutput? AgentOutput { get; init; }

    public ReviewReport? ReviewReport { get; init; }

    public GateDecision? GateDecision { get; init; }

    public RejectReport? RejectReport { get; init; }

    public HumanApprovalResolution? ApprovalResolution { get; init; }

    public int RetryCount { get; init; }

    public int MaxRetries { get; init; }

    public string? NextStepId { get; init; }

    public IReadOnlyList<string> Logs { get; init; }

    public IReadOnlyList<string> Issues { get; init; }

    private static WorkflowStepStatus ParseStatus(string status, GateDecision? decision)
    {
        if (decision is not null)
        {
            return decision.Result switch
            {
                GateDecisionResult.Passed => WorkflowStepStatus.Passed,
                GateDecisionResult.Rejected => WorkflowStepStatus.Rejected,
                GateDecisionResult.Failed => WorkflowStepStatus.Failed,
                GateDecisionResult.NeedsHumanApproval => WorkflowStepStatus.WaitingForHumanApproval,
                _ => WorkflowStepStatus.Running
            };
        }

        if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowStepStatus.Passed;
        }

        return Enum.TryParse<WorkflowStepStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : WorkflowStepStatus.Running;
    }
}
