using DomainSchemas;
using QualityGate;

namespace PlatformCore;

public static class WorkflowReliabilitySelfCheck
{
    public static async Task<bool> RunApprovalIdentityAsync()
    {
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy());
        var context = Context();
        var downstreamCalls = 0;
        var initial = await engine.ExecuteAsync(
        [
            new WorkflowStep("approval", _ => Task.FromResult(Result("approval", GateDecisionResult.NeedsHumanApproval))),
            new WorkflowStep("approval", _ => Task.FromResult(Result("approval", GateDecisionResult.NeedsHumanApproval))),
            new WorkflowStep("downstream", _ =>
            {
                downstreamCalls++;
                return Task.FromResult(Result("downstream", GateDecisionResult.Passed));
            })
        ], context);
        var request = initial.HumanApprovalRequest!;
        var firstSubmission = Submission(request, WorkflowApprovalDecision.Approve, "identity-self-check");
        foreach (var invalid in new[]
        {
            firstSubmission with { ApprovalRequestId = null },
            firstSubmission with { StepId = null },
            firstSubmission with { ApprovalRequestId = "wrong-request" },
            firstSubmission with { StepId = "wrong-step" }
        })
        {
            if ((await engine.SubmitHumanApprovalAsync(invalid)).Accepted ||
                !engine.TryGetPendingHumanApproval(context.TaskId, out var retained) || retained != request) return false;
        }
        var first = await engine.SubmitHumanApprovalAsync(firstSubmission);
        var next = first.WorkflowResult?.HumanApprovalRequest;
        if (!first.Accepted || next is null || next.ApprovalRequestId == request.ApprovalRequestId ||
            (await engine.SubmitHumanApprovalAsync(firstSubmission)).Accepted || downstreamCalls != 0) return false;
        var finalSubmission = Submission(next, WorkflowApprovalDecision.Approve, "identity-self-check");
        var final = await engine.SubmitHumanApprovalAsync(finalSubmission);
        return final.Accepted && final.WorkflowResult?.Status == WorkflowStatus.Passed && downstreamCalls == 1 &&
               final.WorkflowResult.Steps[0].ApprovalResolution?.ApprovalRequestId == request.ApprovalRequestId &&
               final.WorkflowResult.Steps[1].ApprovalResolution?.ApprovalRequestId == next.ApprovalRequestId &&
               !(await engine.SubmitHumanApprovalAsync(finalSubmission)).Accepted;
    }

    public static async Task<IReadOnlyDictionary<string, bool>> RunAsync() =>
        new Dictionary<string, bool>
        {
            ["workflow_step_retry_limit_enforced"] = await CheckRetryLimitsAsync(),
            ["workflow_cancelled_approval_preserved"] = await CheckCancelledApprovalAsync(),
            ["workflow_multi_approval_history_preserved"] = await CheckApprovalHistoryAsync()
        };

    private static async Task<bool> CheckRetryLimitsAsync()
    {
        foreach (var (limit, expectedRetries, issue) in new (int? Limit, int Retries, string Issue)[]
                 { (0, 0, "retryable"), (1, 1, "retryable"), (7, 2, "retryable"),
                   (null, 2, "retryable"), (-1, 0, "retryable"), (7, 0, "non_retryable") })
        {
            var calls = 0;
            var downstreamCalls = 0;
            var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(2));
            var result = await engine.ExecuteAsync(
            [
                new WorkflowStep("retry", _ =>
                {
                    calls++;
                    return Task.FromResult(Result("retry", GateDecisionResult.Rejected, issue));
                }, MaxRetries: limit),
                new WorkflowStep("downstream", _ =>
                {
                    downstreamCalls++;
                    return Task.FromResult(Result("downstream", GateDecisionResult.Passed));
                })
            ], Context());
            var effectiveLimit = Math.Max(0, Math.Min(limit ?? 2, 2));
            if (result.Status != WorkflowStatus.Rejected || calls != expectedRetries + 1 || downstreamCalls != 0 ||
                result.Steps.Any(step => step.MaxRetries != effectiveLimit) ||
                result.Steps.Count(step => step.Status == WorkflowStepStatus.Retrying) != expectedRetries ||
                result.AuditLogs.Count(entry => entry.Action == "workflow_step_retrying") != expectedRetries)
            {
                return false;
            }
        }
        return true;
    }

    private static async Task<bool> CheckCancelledApprovalAsync()
    {
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy());
        var context = Context();
        var calls = 0;
        var paused = await engine.ExecuteAsync(
        [
            new WorkflowStep("approval", _ => Task.FromResult(Result("approval", GateDecisionResult.NeedsHumanApproval))),
            new WorkflowStep("downstream", _ =>
            {
                calls++;
                return Task.FromResult(Result("downstream", GateDecisionResult.Passed));
            })
        ], context);
        var submission = Submission(paused.HumanApprovalRequest!, WorkflowApprovalDecision.Approve, "self-check");
        try
        {
            await engine.SubmitHumanApprovalAsync(submission, new CancellationToken(canceled: true));
            return false;
        }
        catch (OperationCanceledException)
        {
            if (calls != 0 || !engine.TryGetPendingHumanApproval(context.TaskId, out var pending) ||
                pending != paused.HumanApprovalRequest)
            {
                return false;
            }
        }
        var approved = await engine.SubmitHumanApprovalAsync(submission);
        var duplicate = await engine.SubmitHumanApprovalAsync(submission);
        return approved.Accepted && approved.WorkflowResult?.Status == WorkflowStatus.Passed &&
               calls == 1 && !duplicate.Accepted && !engine.TryGetPendingHumanApproval(context.TaskId, out _);
    }

    private static async Task<bool> CheckApprovalHistoryAsync()
    {
        foreach (var decision in Enum.GetValues<WorkflowApprovalDecision>())
        {
            var store = new InMemoryWorkflowApprovalStore();
            var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(), new InMemoryAuditLog(), store);
            var context = Context();
            var calls = new Dictionary<string, int>();
            WorkflowStep Step(string id, GateDecisionResult gate) => new(id, _ =>
            {
                calls[id] = calls.GetValueOrDefault(id) + 1;
                return Task.FromResult(Result(id, gate));
            });
            var initial = await engine.ExecuteAsync(
            [
                Step("prefix", GateDecisionResult.Passed),
                Step("first", GateDecisionResult.NeedsHumanApproval),
                Step("middle", GateDecisionResult.Passed),
                Step("second", GateDecisionResult.NeedsHumanApproval),
                Step("downstream", GateDecisionResult.Passed)
            ], context);
            var first = await engine.SubmitHumanApprovalAsync(Submission(initial.HumanApprovalRequest!, WorkflowApprovalDecision.Approve, "self-check-1"));
            if (!first.Accepted || first.WorkflowResult?.Status != WorkflowStatus.WaitingForHumanApproval ||
                !store.TryGet(context.TaskId, out var pending) ||
                !pending.CompletedSteps.Select(step => step.StepId).SequenceEqual(new[] { "prefix", "first", "middle" }) ||
                !pending.AuditLogs.SequenceEqual(first.WorkflowResult.AuditLogs) || calls.ContainsKey("downstream"))
            {
                return false;
            }
            var submission = Submission(first.WorkflowResult.HumanApprovalRequest!, decision, "self-check-2");
            var second = await engine.SubmitHumanApprovalAsync(submission);
            var result = second.WorkflowResult;
            var approved = decision == WorkflowApprovalDecision.Approve;
            var expectedSteps = approved
                ? new[] { "prefix", "first", "middle", "second", "downstream" }
                : new[] { "prefix", "first", "middle", "second" };
            var expectedAudit = new List<string>
            {
                $"{context.TaskId}:workflow_started",
                "prefix:workflow_step_passed",
                "first:workflow_step_waiting_for_human_approval",
                "first:workflow_human_approval_submitted",
                "first:workflow_human_approval_approved",
                "middle:workflow_step_passed",
                "second:workflow_step_waiting_for_human_approval",
                "second:workflow_human_approval_submitted",
                approved ? "second:workflow_human_approval_approved" : "second:workflow_human_approval_rejected"
            };
            if (approved)
            {
                expectedAudit.Add("downstream:workflow_step_passed");
                expectedAudit.Add($"{context.TaskId}:workflow_passed");
            }
            if (!second.Accepted || result is null ||
                result.Status != (approved ? WorkflowStatus.Passed : WorkflowStatus.Rejected) ||
                !result.Steps.Select(step => step.StepId).SequenceEqual(expectedSteps) ||
                result.Steps[1].Status != WorkflowStepStatus.Passed ||
                result.Steps[3].Status != (approved ? WorkflowStepStatus.Passed : WorkflowStepStatus.Rejected) ||
                !result.AuditLogs.Select(entry => $"{entry.Actor}:{entry.Action}").SequenceEqual(expectedAudit) ||
                calls.Values.Any(count => count != 1) ||
                calls.GetValueOrDefault("downstream") != (approved ? 1 : 0) ||
                (await engine.SubmitHumanApprovalAsync(submission)).Accepted ||
                engine.TryGetPendingHumanApproval(context.TaskId, out _))
            {
                return false;
            }
        }
        return true;
    }

    private static WorkflowApprovalSubmission Submission(HumanApprovalRequest request, WorkflowApprovalDecision decision,
        string submittedBy) => new(request.WorkflowId, decision, submittedBy,
            ApprovalRequestId: request.ApprovalRequestId, StepId: request.StepId);

    private static WorkflowContext Context() => new($"workflow-reliability-{Guid.NewGuid():N}", new Dictionary<string, object?>());

    private static WorkflowStepResult Result(string id, GateDecisionResult gate, params string[] issues) =>
        new(id, id, WorkflowStepStatus.Running, $"{id}: {gate}",
            GateDecision: new GateDecision($"gate-{id}", gate, $"{id}: {gate}"), Issues: issues);
}
