using AgentContracts;
using DomainSchemas;
using PlatformCore;
using QualityGate;

namespace PlatformSelfCheck.Tests;

public sealed class WorkflowApprovalIdentityTests
{
    [Theory]
    [InlineData(null, "approval")]
    [InlineData("", "approval")]
    [InlineData("current", null)]
    [InlineData("current", "")]
    [InlineData("wrong-request", "approval")]
    [InlineData("current", "wrong-step")]
    [InlineData("current", "APPROVAL")]
    public async Task MissingOrMismatchedIdentityDoesNotConsumePendingApproval(string? requestId, string? stepId)
    {
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy());
        var context = Context();
        var paused = await engine.ExecuteAsync([Waiting("approval")], context);
        var request = paused.HumanApprovalRequest!;
        var invalid = new WorkflowApprovalSubmission(context.TaskId, WorkflowApprovalDecision.Approve, "reviewer",
            ApprovalRequestId: requestId == "current" ? request.ApprovalRequestId : requestId, StepId: stepId);

        Assert.False((await engine.SubmitHumanApprovalAsync(invalid)).Accepted);
        Assert.True(engine.TryGetPendingHumanApproval(context.TaskId, out var retained));
        Assert.Equal(request, retained);
        Assert.True((await engine.SubmitHumanApprovalAsync(Submission(request))).Accepted);
    }

    [Fact]
    public async Task OldApprovalCannotApproveNextPauseEvenWhenStepIdRepeats()
    {
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy());
        var context = Context();
        var calls = 0;
        var paused = await engine.ExecuteAsync(
        [
            Waiting("approval"), Waiting("approval"),
            new WorkflowStep("downstream", _ =>
            {
                calls++;
                return Task.FromResult(Result("downstream", GateDecisionResult.Passed));
            })
        ], context);
        var originalSubmission = Submission(paused.HumanApprovalRequest!);
        var next = await engine.SubmitHumanApprovalAsync(originalSubmission);
        var nextRequest = next.WorkflowResult!.HumanApprovalRequest!;
        Assert.Equal(paused.HumanApprovalRequest!.StepId, nextRequest.StepId);
        Assert.NotEqual(paused.HumanApprovalRequest.ApprovalRequestId, nextRequest.ApprovalRequestId);

        Assert.False((await engine.SubmitHumanApprovalAsync(originalSubmission)).Accepted);
        Assert.Equal(0, calls);
        Assert.True(engine.TryGetPendingHumanApproval(context.TaskId, out var retained));
        Assert.Equal(nextRequest, retained);
        var completed = await engine.SubmitHumanApprovalAsync(Submission(nextRequest));
        Assert.Equal(WorkflowStatus.Passed, completed.WorkflowResult!.Status);
        Assert.Equal(1, calls);
        Assert.Equal(paused.HumanApprovalRequest.ApprovalRequestId, completed.WorkflowResult.Steps[0].ApprovalResolution!.ApprovalRequestId);
        Assert.Equal(nextRequest.ApprovalRequestId, completed.WorkflowResult.Steps[1].ApprovalResolution!.ApprovalRequestId);
    }

    [Fact]
    public async Task AtomicStoreAllowsOnlyOneMatchingClaimAndWrongWorkflowDoesNotConsume()
    {
        var store = new InMemoryWorkflowApprovalStore();
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(), new InMemoryAuditLog(), store);
        var context = Context();
        var paused = await engine.ExecuteAsync([Waiting("approval")], context);
        var request = paused.HumanApprovalRequest!;
        Assert.False(store.TryTake("other-workflow", request.ApprovalRequestId!, request.StepId, out _));
        Assert.False(store.TryTake(context.TaskId, "wrong-request", request.StepId, out _));
        Assert.False(store.TryTake(context.TaskId, request.ApprovalRequestId!, "wrong-step", out _));
        var successfulClaims = 0;
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            if (store.TryTake(context.TaskId, request.ApprovalRequestId!, request.StepId, out var claimed))
            {
                Assert.Equal(request, claimed.Request);
                Interlocked.Increment(ref successfulClaims);
            }
        })));
        Assert.Equal(1, successfulClaims);
        Assert.False(store.TryGet(context.TaskId, out _));
    }

    [Theory]
    [InlineData(WorkflowApprovalDecision.Approve)]
    [InlineData(WorkflowApprovalDecision.Reject)]
    [InlineData(WorkflowApprovalDecision.RequestRevision)]
    public async Task ResolutionPreservesOriginalEvidenceAndClaimedIdentity(WorkflowApprovalDecision decision)
    {
        var review = new ReviewReport("review", "reviewer", false, 0.75, ["needs_human_approval: review required"], true, false);
        var original = new AgentOutput(AgentOutputStatus.NeedsHumanApproval, "等待审批", [], review.Issues, [], null, ReviewReport: review);
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy());
        var context = Context();
        var paused = await engine.ExecuteAsync([new WorkflowStep("approval", _ => Task.FromResult(
            Result("approval", GateDecisionResult.NeedsHumanApproval) with { AgentOutput = original, ReviewReport = review, Issues = review.Issues }))], context);
        var request = paused.HumanApprovalRequest!;
        var submittedAt = DateTimeOffset.Parse("2026-09-09T12:00:00Z");
        var submission = Submission(request, decision) with { Comment = "审阅记录", SubmittedAt = submittedAt };

        var completed = await engine.SubmitHumanApprovalAsync(submission);
        var step = Assert.Single(completed.WorkflowResult!.Steps);
        var resolution = Assert.IsType<HumanApprovalResolution>(step.ApprovalResolution);
        Assert.Equal(request.ApprovalRequestId, resolution.ApprovalRequestId);
        Assert.Equal(request.StepId, resolution.StepId);
        Assert.Equal(decision.ToString(), resolution.Decision);
        Assert.Equal("reviewer", resolution.SubmittedBy);
        Assert.Equal("审阅记录", resolution.Comment);
        Assert.Equal(submittedAt, resolution.SubmittedAt);
        Assert.Same(review, resolution.OriginalReviewReport);
        Assert.Equal(review.Issues, resolution.OriginalIssues);
        Assert.Same(original, step.AgentOutput);
        Assert.Same(review, step.ReviewReport);
        Assert.Equal(review.Issues, step.Issues);
        Assert.Contains(completed.WorkflowResult.AuditLogs, entry => entry.Action == "workflow_human_approval_submitted" &&
            entry.Message.Contains($"approval_request_id={request.ApprovalRequestId}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("critical invalid geometry", false)]
    [InlineData("non_retryable invalid input", false)]
    [InlineData("manual review", true)]
    public async Task FatalOrNonRetryableIssuesCannotBecomeApprovable(string issue, bool fatalReview)
    {
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy());
        var context = Context();
        var result = await engine.ExecuteAsync([new WorkflowStep("approval", _ => Task.FromResult(
            Result("approval", GateDecisionResult.NeedsHumanApproval) with
            {
                ReviewReport = new ReviewReport("review", "reviewer", false, 0.5, [issue], true, fatalReview)
            }))], context);
        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.Contains("workflow_human_approval_non_approvable", result.FailureReport!.FailureReason);
        Assert.False(engine.TryGetPendingHumanApproval(context.TaskId, out _));
    }

    [Fact]
    public async Task CancellationAfterClaimFailsWithoutRestoringConsumableApproval()
    {
        using var cancelled = new CancellationTokenSource();
        var store = new CancelOnClaimStore(cancelled);
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(), new InMemoryAuditLog(), store);
        var context = Context();
        var calls = 0;
        var paused = await engine.ExecuteAsync([Waiting("approval"), new WorkflowStep("downstream", _ =>
        {
            calls++;
            return Task.FromResult(Result("downstream", GateDecisionResult.Passed));
        })], context);
        var submission = Submission(paused.HumanApprovalRequest!);

        var completed = await engine.SubmitHumanApprovalAsync(submission, cancelled.Token);
        Assert.True(completed.Accepted);
        Assert.Equal(WorkflowStatus.Failed, completed.WorkflowResult!.Status);
        Assert.Equal(0, calls);
        Assert.Equal(new[] { "approval", "downstream" }, completed.WorkflowResult.Steps.Select(step => step.StepId));
        Assert.Contains("workflow_approval_resume_cancelled", completed.WorkflowResult.FailureReport!.FailureReason);
        Assert.False(engine.TryGetPendingHumanApproval(context.TaskId, out _));
        Assert.False((await engine.SubmitHumanApprovalAsync(submission)).Accepted);
    }

    [Fact]
    public async Task CancellationDuringResumedRetryDelayPreservesCompletedAndAttemptHistory()
    {
        using var cancelled = new CancellationTokenSource();
        var engine = new SequentialWorkflowEngine(new ExponentialBackoffRetryPolicy(baseDelayMs: 10_000));
        var context = Context();
        var calls = 0;
        var paused = await engine.ExecuteAsync([Waiting("approval"),
            new WorkflowStep("middle", _ => Task.FromResult(Result("middle", GateDecisionResult.Passed))),
            new WorkflowStep("retry", _ =>
            {
                calls++;
                cancelled.Cancel();
                return Task.FromResult(Result("retry", GateDecisionResult.Rejected));
            })], context);

        var completed = await engine.SubmitHumanApprovalAsync(Submission(paused.HumanApprovalRequest!), cancelled.Token);
        Assert.Equal(WorkflowStatus.Failed, completed.WorkflowResult!.Status);
        Assert.Equal(1, calls);
        Assert.Equal(new[] { "approval", "middle", "retry", "retry" }, completed.WorkflowResult.Steps.Select(step => step.StepId));
        Assert.Equal(WorkflowStepStatus.Retrying, completed.WorkflowResult.Steps[2].Status);
        Assert.Equal("workflow_approval_resume_cancelled", completed.WorkflowResult.AuditLogs.Last().Action);
        Assert.False(engine.TryGetPendingHumanApproval(context.TaskId, out _));
    }

    [Fact]
    public async Task ApprovalIdentitySelfCheckUsesActualPauseIdentities() =>
        Assert.True(await WorkflowReliabilitySelfCheck.RunApprovalIdentityAsync());

    private static WorkflowStep Waiting(string id) => new(id, _ => Task.FromResult(Result(id, GateDecisionResult.NeedsHumanApproval)));
    private static WorkflowContext Context() => new($"approval-identity-{Guid.NewGuid():N}", new Dictionary<string, object?>());
    private static WorkflowStepResult Result(string id, GateDecisionResult decision) => new(id, id, WorkflowStepStatus.Running, id,
        GateDecision: new GateDecision($"gate-{id}", decision, id));
    private static WorkflowApprovalSubmission Submission(HumanApprovalRequest request, WorkflowApprovalDecision decision = WorkflowApprovalDecision.Approve) =>
        new(request.WorkflowId, decision, "reviewer", ApprovalRequestId: request.ApprovalRequestId, StepId: request.StepId);

    private sealed class CancelOnClaimStore(CancellationTokenSource cancellation) : IWorkflowApprovalStore
    {
        private readonly InMemoryWorkflowApprovalStore _inner = new();
        public void Save(PendingWorkflowApproval pending) => _inner.Save(pending);
        public bool TryGet(string workflowId, out PendingWorkflowApproval pending) => _inner.TryGet(workflowId, out pending);
        public bool TryTake(string workflowId, string approvalRequestId, string stepId, out PendingWorkflowApproval pending)
        {
            var claimed = _inner.TryTake(workflowId, approvalRequestId, stepId, out pending);
            if (claimed) cancellation.Cancel();
            return claimed;
        }
    }
}
