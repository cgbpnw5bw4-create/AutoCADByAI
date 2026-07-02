using DomainSchemas;
using QualityGate;

namespace PlatformCore;

public sealed class SequentialWorkflowEngine
{
    private readonly IRetryPolicy _retryPolicy;
    private readonly InMemoryAuditLog _auditLog;
    private readonly RejectReportBuilder _rejectReportBuilder;

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

    public SequentialWorkflowEngine(IRetryPolicy retryPolicy, InMemoryAuditLog auditLog)
    {
        _retryPolicy = retryPolicy;
        _auditLog = auditLog;
        _rejectReportBuilder = new RejectReportBuilder();
    }

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        IEnumerable<WorkflowStep> steps,
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var workflowId = context.TaskId;
        var results = new List<WorkflowStepResult>();
        var orderedSteps = steps.ToArray();
        var auditStartIndex = _auditLog.GetEntries().Count;

        _auditLog.Record("workflow", workflowId, "workflow_started", $"Workflow {workflowId} started.");

        for (var index = 0; index < orderedSteps.Length; index++)
        {
            var step = orderedSteps[index];
            var retryCount = 0;
            var maxRetries = step.MaxRetries ?? _retryPolicy.MaxRetries;
            var nextStepId = step.NextStepId ?? orderedSteps.ElementAtOrDefault(index + 1)?.StepId ?? orderedSteps.ElementAtOrDefault(index + 1)?.Name;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WorkflowStepResult rawResult;
                try
                {
                    rawResult = await step.ExecuteAsync(context);
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

                switch (decision.Result)
                {
                    case GateDecisionResult.Passed:
                    {
                        var passed = Normalize(rawResult, step, WorkflowStepStatus.Passed, decision, retryCount, maxRetries, nextStepId);
                        results.Add(passed);
                        _auditLog.Record("workflow", passed.StepId, "workflow_step_passed", $"Workflow step '{passed.StepId}' passed.");
                        break;
                    }

                    case GateDecisionResult.Rejected when _retryPolicy.ShouldRetry(decision, retryCount, typedIssues):
                    {
                        var retrying = Normalize(rawResult, step, WorkflowStepStatus.Retrying, decision, retryCount, maxRetries, step.StepId ?? step.Name);
                        results.Add(retrying);
                        var retryDelay = _retryPolicy.GetDelay(retryCount);
                        _auditLog.Record("workflow", retrying.StepId, "workflow_step_retrying", $"Workflow step '{retrying.StepId}' rejected; retry {retryCount + 1} of {maxRetries} will run after calculated delay {retryDelay.TotalMilliseconds:0}ms.");
                        retryCount++;
                        if (retryDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(retryDelay, cancellationToken);
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
                            AuditLogs: CurrentAuditLogs(auditStartIndex),
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
                            AuditLogs: CurrentAuditLogs(auditStartIndex),
                            FinalMessage: failureReport.FailureReason);
                    }

                    case GateDecisionResult.NeedsHumanApproval:
                    {
                        var waiting = Normalize(rawResult, step, WorkflowStepStatus.WaitingForHumanApproval, decision, retryCount, maxRetries, null);
                        results.Add(waiting);
                        var request = BuildHumanApprovalRequest(workflowId, waiting, decision);
                        _auditLog.Record("workflow", waiting.StepId, "workflow_step_waiting_for_human_approval", $"Workflow step '{waiting.StepId}' is waiting for human approval.");

                        return new WorkflowExecutionResult(
                            workflowId,
                            WorkflowStatus.WaitingForHumanApproval,
                            results,
                            decision,
                            HumanApprovalRequest: request,
                            AuditLogs: CurrentAuditLogs(auditStartIndex),
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
            AuditLogs: CurrentAuditLogs(auditStartIndex),
            FinalMessage: "Workflow completed all steps.");
    }

    private IReadOnlyList<AuditLogEntry> CurrentAuditLogs(int auditStartIndex) =>
        _auditLog.GetEntries().Skip(auditStartIndex).ToArray();

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
            DateTimeOffset.UtcNow);
}
