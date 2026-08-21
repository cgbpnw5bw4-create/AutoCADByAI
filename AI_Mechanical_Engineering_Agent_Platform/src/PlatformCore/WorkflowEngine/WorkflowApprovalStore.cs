using System.Collections.Concurrent;
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

    bool TryTake(string workflowId, out PendingWorkflowApproval pending);
}

public sealed class InMemoryWorkflowApprovalStore : IWorkflowApprovalStore
{
    private readonly ConcurrentDictionary<string, PendingWorkflowApproval> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    public void Save(PendingWorkflowApproval pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (!_pending.TryAdd(pending.WorkflowId, pending))
        {
            throw new InvalidOperationException(
                $"workflow_approval_already_pending: {pending.WorkflowId} already has an unresolved approval request.");
        }
    }

    public bool TryGet(string workflowId, out PendingWorkflowApproval pending) =>
        _pending.TryGetValue(workflowId, out pending!);

    public bool TryTake(string workflowId, out PendingWorkflowApproval pending) =>
        _pending.TryRemove(workflowId, out pending!);
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
    DateTimeOffset? SubmittedAt = null);

public sealed record WorkflowApprovalSubmissionResult(
    bool Accepted,
    WorkflowExecutionResult? WorkflowResult,
    string? FailureReason = null);
