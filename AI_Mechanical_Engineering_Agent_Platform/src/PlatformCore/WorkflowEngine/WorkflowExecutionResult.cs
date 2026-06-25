using DomainSchemas;

namespace PlatformCore;

public sealed record WorkflowExecutionResult
{
    public WorkflowExecutionResult(
        string WorkflowId,
        WorkflowStatus Status,
        IReadOnlyList<WorkflowStepResult> Steps,
        GateDecision? FinalGateDecision = null,
        FailureReport? FailureReport = null,
        HumanApprovalRequest? HumanApprovalRequest = null,
        IReadOnlyList<AuditLogEntry>? AuditLogs = null,
        string? FinalMessage = null)
    {
        this.WorkflowId = WorkflowId;
        this.Status = Status;
        this.Steps = Steps;
        this.FinalGateDecision = FinalGateDecision;
        this.FailureReport = FailureReport;
        this.HumanApprovalRequest = HumanApprovalRequest;
        this.AuditLogs = AuditLogs ?? Array.Empty<AuditLogEntry>();
        this.FinalMessage = FinalMessage ?? ResolveFinalMessage(Status);
    }

    public WorkflowExecutionResult(
        string TaskId,
        string FinalStatus,
        IReadOnlyList<WorkflowStepResult> Steps)
        : this(TaskId, ParseStatus(FinalStatus), Steps, Steps.LastOrDefault()?.GateDecision)
    {
    }

    public string WorkflowId { get; init; }

    public WorkflowStatus Status { get; init; }

    public IReadOnlyList<WorkflowStepResult> Steps { get; init; }

    public GateDecision? FinalGateDecision { get; init; }

    public FailureReport? FailureReport { get; init; }

    public HumanApprovalRequest? HumanApprovalRequest { get; init; }

    public IReadOnlyList<AuditLogEntry> AuditLogs { get; init; }

    public string FinalMessage { get; init; }

    public string TaskId => WorkflowId;

    public string FinalStatus => Status.ToString();

    private static WorkflowStatus ParseStatus(string status) =>
        Enum.TryParse<WorkflowStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : WorkflowStatus.Failed;

    private static string ResolveFinalMessage(WorkflowStatus status) =>
        status switch
        {
            WorkflowStatus.Passed => "Workflow passed.",
            WorkflowStatus.Rejected => "Workflow rejected by QualityGate.",
            WorkflowStatus.Failed => "Workflow failed.",
            WorkflowStatus.WaitingForHumanApproval => "Workflow is waiting for human approval.",
            _ => $"Workflow status is {status}."
        };
}
