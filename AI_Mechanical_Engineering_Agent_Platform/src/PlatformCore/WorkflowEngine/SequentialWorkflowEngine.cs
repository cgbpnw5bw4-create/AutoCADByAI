using DomainSchemas;
using QualityGate;

namespace PlatformCore;

public sealed class SequentialWorkflowEngine
{
    private readonly IRetryPolicy _retryPolicy;
    private readonly InMemoryAuditLog _auditLog;
    private readonly RejectReportBuilder _rejectReportBuilder;
    private readonly IWorkflowApprovalStore _approvalStore;

    public SequentialWorkflowEngine()
        : this(CreateDefaultRetryPolicy(), new InMemoryAuditLog())
    {
    }

    public static IRetryPolicy CreateDefaultRetryPolicy() =>
        new ExponentialBackoffRetryPolicy(maxRetries: 2, baseDelayMs: 500, maxDelayMs: 3_000);

    public SequentialWorkflowEngine(IRetryPolicy retryPolicy)
        : this(retryPolicy, new InMemoryAuditLog())
    {
    }

    public SequentialWorkflowEngine(
        IRetryPolicy retryPolicy,
        InMemoryAuditLog auditLog,
        IWorkflowApprovalStore? approvalStore = null)
    {
        _retryPolicy = retryPolicy;
        _auditLog = auditLog;
        _rejectReportBuilder = new RejectReportBuilder();
        _approvalStore = approvalStore ?? new InMemoryWorkflowApprovalStore();
    }

    public bool TryGetPendingHumanApproval(
        string workflowId,
        out HumanApprovalRequest request)
    {
        request = null!;
        if (string.IsNullOrWhiteSpace(workflowId) ||
            !_approvalStore.TryGet(workflowId, out var pending))
        {
            return false;
        }

        request = pending.Request;
        return true;
    }

    public async Task<WorkflowApprovalSubmissionResult> SubmitHumanApprovalAsync(
        WorkflowApprovalSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (string.IsNullOrWhiteSpace(submission.WorkflowId) ||
            string.IsNullOrWhiteSpace(submission.SubmittedBy) ||
            string.IsNullOrWhiteSpace(submission.ApprovalRequestId) ||
            string.IsNullOrWhiteSpace(submission.StepId))
        {
            return new(false, null, "workflow_approval_invalid_submission: workflow_id, submitted_by, approval_request_id and step_id are required.");
        }

        if (!Enum.IsDefined(submission.Decision))
        {
            return new(false, null, "workflow_approval_invalid_submission: unsupported approval decision.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_approvalStore.TryTake(submission.WorkflowId, submission.ApprovalRequestId, submission.StepId, out var pending))
        {
            return new(false, null, $"workflow_approval_not_pending_or_mismatched: {submission.WorkflowId} has no pending approval matching request {submission.ApprovalRequestId} and step {submission.StepId}.");
        }

        var auditStartIndex = _auditLog.GetEntries().Count;
        var submittedAt = submission.SubmittedAt ?? DateTimeOffset.UtcNow;
        submission = submission with { SubmittedAt = submittedAt };
        _auditLog.Record(
            "workflow",
            pending.WaitingStep.StepId,
            "workflow_human_approval_submitted",
            $"Workflow {pending.WorkflowId} received {submission.Decision} from {submission.SubmittedBy} at {submittedAt:O}; approval_request_id={submission.ApprovalRequestId}; step_id={submission.StepId}.");

        return submission.Decision switch
        {
            WorkflowApprovalDecision.Approve => await ResumeApprovedAsync(
                pending,
                submission,
                auditStartIndex,
                cancellationToken),
            WorkflowApprovalDecision.Reject or WorkflowApprovalDecision.RequestRevision =>
                CompleteRejectedApproval(pending, submission, auditStartIndex),
            _ => new(false, null, "workflow_approval_invalid_submission: unsupported approval decision.")
        };
    }

    public Task<WorkflowExecutionResult> ExecuteAsync(
        IEnumerable<WorkflowStep> steps,
        WorkflowContext context,
        CancellationToken cancellationToken = default) =>
        ExecuteStepsAsync(steps, context, Array.Empty<WorkflowStepResult>(), Array.Empty<AuditLogEntry>(), true, cancellationToken);

    private async Task<WorkflowExecutionResult> ExecuteStepsAsync(
        IEnumerable<WorkflowStep> steps,
        WorkflowContext context,
        IReadOnlyList<WorkflowStepResult> completedSteps,
        IReadOnlyList<AuditLogEntry> auditHistory,
        bool isInitialExecution,
        CancellationToken cancellationToken)
    {
        var workflowId = context.TaskId;
        var results = completedSteps.ToList();
        var orderedSteps = steps.ToArray();
        var auditStartIndex = _auditLog.GetEntries().Count;
        IReadOnlyList<AuditLogEntry> SnapshotAuditLogs() =>
            auditHistory.Concat(CurrentAuditLogs(auditStartIndex)).ToArray();
        WorkflowExecutionResult CancelledResume(WorkflowStep step, int retryCount, int maxRetries)
        {
            const string stage = "workflow_approval_resume_cancelled";
            var stepId = step.StepId ?? step.Name;
            var reason = $"{stage}: Approved continuation was cancelled at step '{stepId}'. Inspect completed steps and side effects before starting a new workflow; automatic replay is disabled.";
            var decision = new GateDecision($"gate-{stepId}-cancelled-{Guid.NewGuid():N}", GateDecisionResult.Failed, reason);
            var cancelled = new WorkflowStepResult(stepId, step.Name, WorkflowStepStatus.Failed, reason,
                GateDecision: decision, Issues: [reason], Logs: [reason], RetryCount: retryCount, MaxRetries: maxRetries);
            results.Add(cancelled);
            _auditLog.Record("workflow", stepId, stage, reason);
            return new WorkflowExecutionResult(workflowId, WorkflowStatus.Failed, results, decision,
                FailureReport: BuildFailureReport(workflowId, cancelled, decision), AuditLogs: SnapshotAuditLogs(), FinalMessage: reason);
        }

        if (isInitialExecution)
        {
            _auditLog.Record("workflow", workflowId, "workflow_started", $"Workflow {workflowId} started.");
        }

        for (var index = 0; index < orderedSteps.Length; index++)
        {
            var step = orderedSteps[index];
            var retryCount = 0;
            // 步骤上限只能收紧全局策略；负上限按不允许重试处理。
            var maxRetries = Math.Max(0, Math.Min(step.MaxRetries ?? _retryPolicy.MaxRetries, _retryPolicy.MaxRetries));
            var nextStepId = step.NextStepId ?? orderedSteps.ElementAtOrDefault(index + 1)?.StepId ?? orderedSteps.ElementAtOrDefault(index + 1)?.Name;

            while (true)
            {
                WorkflowStepResult rawResult;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    rawResult = await step.ExecuteAsync(context);
                }
                catch (OperationCanceledException) when (!isInitialExecution)
                {
                    return CancelledResume(step, retryCount, maxRetries);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    rawResult = BuildExceptionResult(step, ex);
                }

                var decision = rawResult.GateDecision ?? PassedDecision(step);
                var issues = ResolveIssues(rawResult);
                var typedIssues = issues.Select(Issue.FromText).ToArray();
                if (decision.Result == GateDecisionResult.NeedsHumanApproval && HasNonApprovableIssues(rawResult))
                {
                    decision = new GateDecision($"gate-{step.StepId ?? step.Name}-non-approvable-{Guid.NewGuid():N}",
                        GateDecisionResult.Failed,
                        "workflow_human_approval_non_approvable: Fatal, critical or non_retryable issues require repair and cannot be approved.");
                }

                switch (decision.Result)
                {
                    case GateDecisionResult.Passed:
                    {
                        var passed = Normalize(rawResult, step, WorkflowStepStatus.Passed, decision, retryCount, maxRetries, nextStepId);
                        results.Add(passed);
                        _auditLog.Record("workflow", passed.StepId, "workflow_step_passed", $"Workflow step '{passed.StepId}' passed.");
                        break;
                    }

                    case GateDecisionResult.Rejected when retryCount < maxRetries &&
                                                         _retryPolicy.ShouldRetry(decision, retryCount, typedIssues):
                    {
                        var retrying = Normalize(rawResult, step, WorkflowStepStatus.Retrying, decision, retryCount, maxRetries, step.StepId ?? step.Name);
                        results.Add(retrying);
                        var retryDelay = _retryPolicy.GetDelay(retryCount);
                        _auditLog.Record("workflow", retrying.StepId, "workflow_step_retrying", $"Workflow step '{retrying.StepId}' rejected; retry {retryCount + 1} of {maxRetries} will run after calculated delay {retryDelay.TotalMilliseconds:0}ms.");
                        retryCount++;
                        if (retryDelay > TimeSpan.Zero)
                        {
                            try
                            {
                                await Task.Delay(retryDelay, cancellationToken);
                            }
                            catch (OperationCanceledException) when (!isInitialExecution)
                            {
                                return CancelledResume(step, retryCount, maxRetries);
                            }
                        }

                        continue;
                    }

                    case GateDecisionResult.Rejected:
                    {
                        var review = ResolveReviewReport(rawResult, step, decision, issues);
                        var rejectReport = rawResult.RejectReport ?? decision.RejectReport ?? _rejectReportBuilder.Build(review, decision);
                        var rejected = Normalize(rawResult, step, WorkflowStepStatus.Rejected, decision, retryCount, maxRetries, null) with
                        {
                            RejectReport = rejectReport,
                            ReviewReport = review
                        };
                        results.Add(rejected);
                        var failureReport = BuildFailureReport(workflowId, rejected, decision);
                        _auditLog.Record("workflow", rejected.StepId, "workflow_step_rejected", $"Workflow step '{rejected.StepId}' exceeded retry policy.");

                        return new WorkflowExecutionResult(
                            workflowId,
                            WorkflowStatus.Rejected,
                            results,
                            decision,
                            FailureReport: failureReport,
                            AuditLogs: SnapshotAuditLogs(),
                            FinalMessage: rejectReport.Message);
                    }

                    case GateDecisionResult.Failed:
                    {
                        var failed = Normalize(rawResult, step, WorkflowStepStatus.Failed, decision, retryCount, maxRetries, null);
                        results.Add(failed);
                        var failureReport = BuildFailureReport(workflowId, failed, decision);
                        _auditLog.Record("workflow", failed.StepId, "workflow_step_failed", $"Workflow step '{failed.StepId}' failed.");

                        return new WorkflowExecutionResult(
                            workflowId,
                            WorkflowStatus.Failed,
                            results,
                            decision,
                            FailureReport: failureReport,
                            AuditLogs: SnapshotAuditLogs(),
                            FinalMessage: failureReport.FailureReason);
                    }

                    case GateDecisionResult.NeedsHumanApproval:
                    {
                        var waiting = Normalize(rawResult, step, WorkflowStepStatus.WaitingForHumanApproval, decision, retryCount, maxRetries, null);
                        results.Add(waiting);
                        var request = BuildHumanApprovalRequest(workflowId, waiting, decision);
                        _auditLog.Record("workflow", waiting.StepId, "workflow_step_waiting_for_human_approval", $"Workflow step '{waiting.StepId}' is waiting for human approval.");
                        _approvalStore.Save(new PendingWorkflowApproval(
                            workflowId,
                            context,
                            results.Take(results.Count - 1).ToArray(),
                            waiting,
                            orderedSteps.Skip(index + 1).ToArray(),
                            request,
                            SnapshotAuditLogs()));

                        return new WorkflowExecutionResult(
                            workflowId,
                            WorkflowStatus.WaitingForHumanApproval,
                            results,
                            decision,
                            HumanApprovalRequest: request,
                            AuditLogs: SnapshotAuditLogs(),
                            FinalMessage: request.Reason);
                    }
                }

                break;
            }
        }

        var finalDecision = results.LastOrDefault()?.GateDecision ?? new GateDecision(
            $"gate-{workflowId}-passed",
            GateDecisionResult.Passed,
            "Workflow completed all steps.");
        _auditLog.Record("workflow", workflowId, "workflow_passed", $"Workflow {workflowId} passed.");

        return new WorkflowExecutionResult(
            workflowId,
            WorkflowStatus.Passed,
            results,
            finalDecision,
            AuditLogs: SnapshotAuditLogs(),
            FinalMessage: "Workflow completed all steps.");
    }

    private IReadOnlyList<AuditLogEntry> CurrentAuditLogs(int auditStartIndex) =>
        _auditLog.GetEntries().Skip(auditStartIndex).ToArray();

    private async Task<WorkflowApprovalSubmissionResult> ResumeApprovedAsync(
        PendingWorkflowApproval pending,
        WorkflowApprovalSubmission submission,
        int auditStartIndex,
        CancellationToken cancellationToken)
    {
        var approvalDecision = new GateDecision(
            $"gate-{pending.WaitingStep.StepId}-human-approved-{Guid.NewGuid():N}",
            GateDecisionResult.Passed,
            $"Human approval granted by {submission.SubmittedBy}.");
        var approvedStep = pending.WaitingStep with
        {
            Status = WorkflowStepStatus.Passed,
            GateDecision = approvalDecision,
            ApprovalResolution = BuildApprovalResolution(pending, submission),
            NextStepId = pending.RemainingSteps.FirstOrDefault()?.StepId ??
                         pending.RemainingSteps.FirstOrDefault()?.Name,
            Logs = pending.WaitingStep.Logs
                .Append($"workflow_human_approval_approved: submitted_by={submission.SubmittedBy}; comment={submission.Comment ?? string.Empty}")
                .ToArray()
        };
        _auditLog.Record(
            "workflow",
            approvedStep.StepId,
            "workflow_human_approval_approved",
            $"Workflow {pending.WorkflowId} resumed after approval from {submission.SubmittedBy}.");

        var result = await ExecuteStepsAsync(
            pending.RemainingSteps,
            pending.Context,
            pending.CompletedSteps.Append(approvedStep).ToArray(),
            pending.AuditLogs.Concat(CurrentAuditLogs(auditStartIndex)).ToArray(),
            false,
            cancellationToken);
        return new(true, result);
    }

    private WorkflowApprovalSubmissionResult CompleteRejectedApproval(
        PendingWorkflowApproval pending,
        WorkflowApprovalSubmission submission,
        int auditStartIndex)
    {
        var decision = new GateDecision(
            $"gate-{pending.WaitingStep.StepId}-human-{submission.Decision.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}",
            GateDecisionResult.Rejected,
            submission.Decision == WorkflowApprovalDecision.Reject
                ? $"Human approval rejected by {submission.SubmittedBy}."
                : $"Human requested revision: {submission.SubmittedBy}.");
        var rejectedStep = pending.WaitingStep with
        {
            Status = WorkflowStepStatus.Rejected,
            GateDecision = decision,
            ApprovalResolution = BuildApprovalResolution(pending, submission),
            NextStepId = null,
            Logs = pending.WaitingStep.Logs
                .Append($"workflow_human_approval_{submission.Decision.ToString().ToLowerInvariant()}: submitted_by={submission.SubmittedBy}; comment={submission.Comment ?? string.Empty}")
                .ToArray()
        };
        var failure = BuildFailureReport(pending.WorkflowId, rejectedStep, decision);
        _auditLog.Record(
            "workflow",
            rejectedStep.StepId,
            "workflow_human_approval_rejected",
            $"Workflow {pending.WorkflowId} stopped after {submission.Decision} from {submission.SubmittedBy}.");
        return new(
            true,
            new WorkflowExecutionResult(
                pending.WorkflowId,
                WorkflowStatus.Rejected,
                pending.CompletedSteps.Append(rejectedStep).ToArray(),
                decision,
                FailureReport: failure,
                AuditLogs: pending.AuditLogs.Concat(CurrentAuditLogs(auditStartIndex)).ToArray(),
                FinalMessage: failure.FailureReason));
    }

    private static HumanApprovalResolution BuildApprovalResolution(
        PendingWorkflowApproval pending,
        WorkflowApprovalSubmission submission) =>
        new(pending.Request.ApprovalRequestId!, pending.Request.StepId, submission.Decision.ToString(),
            submission.SubmittedBy, submission.Comment, submission.SubmittedAt!.Value,
            pending.WaitingStep.AgentOutput?.ReviewReport ?? pending.WaitingStep.ReviewReport,
            AllOriginalIssues(pending.WaitingStep).Distinct(StringComparer.Ordinal).ToArray());

    private static IEnumerable<string> AllOriginalIssues(WorkflowStepResult result) =>
        result.Issues.Concat(result.AgentOutput?.Issues ?? Array.Empty<string>())
            .Concat(result.ReviewReport?.Issues ?? Array.Empty<string>())
            .Concat(result.AgentOutput?.ReviewReport?.Issues ?? Array.Empty<string>());

    private static bool HasNonApprovableIssues(WorkflowStepResult result) =>
        result.ReviewReport?.HasFatalError == true || result.AgentOutput?.ReviewReport?.HasFatalError == true ||
        AllOriginalIssues(result).Select(Issue.FromText).Any(issue =>
            issue.Severity == "critical" || issue.Type == "non_retryable");

    private static WorkflowStepResult Normalize(
        WorkflowStepResult rawResult,
        WorkflowStep step,
        WorkflowStepStatus status,
        GateDecision decision,
        int retryCount,
        int maxRetries,
        string? nextStepId)
    {
        var stepId = step.StepId ?? rawResult.StepId;
        return rawResult with
        {
            StepId = stepId,
            StepName = step.Name,
            Status = status,
            GateDecision = decision,
            RetryCount = retryCount,
            MaxRetries = maxRetries,
            NextStepId = nextStepId,
            Issues = ResolveIssues(rawResult),
            Logs = rawResult.Logs
        };
    }

    private static GateDecision PassedDecision(WorkflowStep step) =>
        new(
            $"gate-{step.StepId ?? step.Name}-{Guid.NewGuid():N}",
            GateDecisionResult.Passed,
            $"Workflow step '{step.Name}' did not request rejection, failure or human approval.");

    private static WorkflowStepResult BuildExceptionResult(WorkflowStep step, Exception exception)
    {
        var rootException = exception.GetBaseException();
        var stepId = step.StepId ?? step.Name;
        var issue = $"workflow_step_exception: {rootException.GetType().Name}: {rootException.Message}";
        var decision = new GateDecision(
            $"gate-{stepId}-exception-{Guid.NewGuid():N}",
            GateDecisionResult.Failed,
            $"Workflow step '{step.Name}' threw an exception.");

        return new WorkflowStepResult(
            stepId,
            step.Name,
            WorkflowStepStatus.Failed,
            $"Workflow step '{step.Name}' failed with an exception.",
            GateDecision: decision,
            Logs: new[]
            {
                $"workflow_step_exception: step_id={stepId}, exception_type={rootException.GetType().FullName}, message={rootException.Message}"
            },
            Issues: new[] { issue });
    }

    private static IReadOnlyList<string> ResolveIssues(WorkflowStepResult result)
    {
        if (result.Issues.Count > 0)
        {
            return result.Issues;
        }

        if (result.AgentOutput?.Issues.Count > 0)
        {
            return result.AgentOutput.Issues;
        }

        if (result.ReviewReport?.Issues.Count > 0)
        {
            return result.ReviewReport.Issues;
        }

        return Array.Empty<string>();
    }

    private static ReviewReport ResolveReviewReport(
        WorkflowStepResult result,
        WorkflowStep step,
        GateDecision decision,
        IReadOnlyList<string> issues) =>
        result.ReviewReport ?? new ReviewReport(
            $"review-{step.StepId ?? step.Name}-{Guid.NewGuid():N}",
            "workflow-engine",
            IsPassed: decision.Result == GateDecisionResult.Passed,
            Score: decision.Result == GateDecisionResult.Passed ? 1.0 : 0.0,
            Issues: issues,
            RequiresHumanApproval: decision.Result == GateDecisionResult.NeedsHumanApproval,
            HasFatalError: decision.Result == GateDecisionResult.Failed);

    private static FailureReport BuildFailureReport(
        string workflowId,
        WorkflowStepResult failed,
        GateDecision decision) =>
        new(
            workflowId,
            failed.StepId,
            failed.StepName,
            failed.AgentOutput?.NextRecommendedAgentId ?? "unknown",
            decision.Reason,
            failed.Issues,
            failed.Logs,
            "Stop automatic execution and route to error diagnosis.");

    private static HumanApprovalRequest BuildHumanApprovalRequest(
        string workflowId,
        WorkflowStepResult waiting,
        GateDecision decision) =>
        new(
            workflowId,
            waiting.StepId,
            waiting.StepName,
            "workflow-engine",
            decision.Reason,
            new[] { "approve", "reject", "request_revision" },
            waiting.Message,
            DateTimeOffset.UtcNow,
            $"approval-{Guid.NewGuid():N}");
}
