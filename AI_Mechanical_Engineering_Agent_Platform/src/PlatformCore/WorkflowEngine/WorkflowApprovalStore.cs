using DomainSchemas;

namespace PlatformCore;

/// <summary>
/// External approval boundary for an in-process workflow pause. A production
/// host may replace this store with durable infrastructure; the engine never
/// resumes a paused workflow unless an explicit submission is recorded.
/// </summary>
public interface IWorkflowApprovalStore
{
    void Save(PendingWorkflowApproval pending);

    bool TryGet(string workflowId, out PendingWorkflowApproval pending);

    bool TryTake(string workflowId, string approvalRequestId, string stepId, out PendingWorkflowApproval pending);
}

public sealed class InMemoryWorkflowApprovalStore : IWorkflowApprovalStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PendingWorkflowApproval> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    public void Save(PendingWorkflowApproval pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (string.IsNullOrWhiteSpace(pending.Request.ApprovalRequestId) ||
            !string.Equals(pending.Request.WorkflowId, pending.WorkflowId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(pending.Request.StepId, pending.WaitingStep.StepId, StringComparison.Ordinal))
        {
            throw new ArgumentException("workflow_approval_invalid_identity: pending approval must bind its workflow, request and waiting step.", nameof(pending));
        }
        lock (_sync)
        {
            if (!_pending.TryAdd(pending.WorkflowId, pending))
            {
                throw new InvalidOperationException(
                    $"workflow_approval_already_pending: {pending.WorkflowId} already has an unresolved approval request.");
            }
        }
    }

    public bool TryGet(string workflowId, out PendingWorkflowApproval pending)
    {
        lock (_sync) return _pending.TryGetValue(workflowId, out pending!);
    }

    public bool TryTake(string workflowId, string approvalRequestId, string stepId, out PendingWorkflowApproval pending)
    {
        lock (_sync)
        {
            pending = null!;
            if (!_pending.TryGetValue(workflowId, out var candidate) ||
                !string.Equals(candidate.Request.ApprovalRequestId, approvalRequestId, StringComparison.Ordinal) ||
                !string.Equals(candidate.WaitingStep.StepId, stepId, StringComparison.Ordinal)) return false;
            _pending.Remove(workflowId);
            pending = candidate;
            return true;
        }
    }
}

public sealed record PendingWorkflowApproval(
    string WorkflowId,
    WorkflowContext Context,
    IReadOnlyList<WorkflowStepResult> CompletedSteps,
    WorkflowStepResult WaitingStep,
    IReadOnlyList<WorkflowStep> RemainingSteps,
    HumanApprovalRequest Request,
    IReadOnlyList<AuditLogEntry> AuditLogs);

public enum WorkflowApprovalDecision
{
    Approve,
    Reject,
    RequestRevision
}

public sealed record WorkflowApprovalSubmission(
    string WorkflowId,
    WorkflowApprovalDecision Decision,
    string SubmittedBy,
    string? Comment = null,
    DateTimeOffset? SubmittedAt = null,
    string? ApprovalRequestId = null,
    string? StepId = null);

public sealed record WorkflowApprovalSubmissionResult(
    bool Accepted,
    WorkflowExecutionResult? WorkflowResult,
    string? FailureReason = null);
