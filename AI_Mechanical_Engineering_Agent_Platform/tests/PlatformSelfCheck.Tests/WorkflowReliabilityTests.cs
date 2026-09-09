using DomainSchemas;
using PlatformCore;
using QualityGate;

namespace PlatformSelfCheck.Tests;

public sealed class WorkflowReliabilityTests
{
    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(7, 2, 2)]
    [InlineData(null, 2, 2)]
    [InlineData(-1, 2, 0)]
    [InlineData(2, -1, 0)]
    public async Task StepRetryLimitTightensPolicyAndMatchesResultsAndAudit(
        int? stepLimit, int policyLimit, int effectiveLimit)
    {
        var attempts = 0;
        var downstreamCalls = 0;
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(policyLimit));

        var result = await engine.ExecuteAsync(
        [
            new WorkflowStep("retry", _ =>
            {
                attempts++;
                return Task.FromResult(Result("retry", GateDecisionResult.Rejected));
            }, MaxRetries: stepLimit),
            new WorkflowStep("downstream", _ =>
            {
                downstreamCalls++;
                return Task.FromResult(Result("downstream", GateDecisionResult.Passed));
            })
        ], Context());

        Assert.Equal(WorkflowStatus.Rejected, result.Status);
        Assert.Equal(effectiveLimit + 1, attempts);
        Assert.Equal(0, downstreamCalls);
        Assert.All(result.Steps, step => Assert.Equal(effectiveLimit, step.MaxRetries));
        Assert.Equal(Enumerable.Range(0, effectiveLimit + 1), result.Steps.Select(step => step.RetryCount));
        Assert.Equal(effectiveLimit, result.Steps.Count(step => step.Status == WorkflowStepStatus.Retrying));
        var retries = result.AuditLogs.Where(entry => entry.Action == "workflow_step_retrying").ToArray();
        Assert.Equal(effectiveLimit, retries.Length);
        for (var index = 0; index < retries.Length; index++)
        {
            Assert.Contains($"retry {index + 1} of {effectiveLimit}", retries[index].Message);
        }
    }

    [Theory]
    [InlineData("non_retryable invalid input")]
    [InlineData("critical unsafe geometry")]
    public async Task StepLimitCannotOverrideNonRetryablePolicy(string issue)
    {
        var attempts = 0;
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(2));
        var result = await engine.ExecuteAsync(
        [
            new WorkflowStep("blocked", _ =>
            {
                attempts++;
                return Task.FromResult(Result("blocked", GateDecisionResult.Rejected, issue));
            }, MaxRetries: 10)
        ], Context());

        Assert.Equal(WorkflowStatus.Rejected, result.Status);
        Assert.Equal(1, attempts);
        Assert.Equal(2, Assert.Single(result.Steps).MaxRetries);
        Assert.DoesNotContain(result.AuditLogs, entry => entry.Action == "workflow_step_retrying");
    }

    [Fact]
    public async Task PreCancelledSubmissionDoesNotTakeApprovalAndNormalSubmissionStillRunsOnce()
    {
        var store = new CountingApprovalStore();
        var audit = new InMemoryAuditLog();
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(), audit, store);
        var context = Context();
        var downstreamCalls = 0;
        var paused = await engine.ExecuteAsync(
        [
            new WorkflowStep("approval", _ => Task.FromResult(Result("approval", GateDecisionResult.NeedsHumanApproval))),
            new WorkflowStep("downstream", _ =>
            {
                downstreamCalls++;
                return Task.FromResult(Result("downstream", GateDecisionResult.Passed));
            })
        ], context);
        var submission = Submission(engine, context.TaskId, WorkflowApprovalDecision.Approve, "reviewer");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.SubmitHumanApprovalAsync(submission, new CancellationToken(canceled: true)));

        Assert.Equal(0, store.TakeCalls);
        Assert.True(engine.TryGetPendingHumanApproval(context.TaskId, out var retained));
        Assert.Equal(paused.HumanApprovalRequest, retained);
        Assert.Equal(paused.AuditLogs, audit.GetEntries());
        Assert.Equal(0, downstreamCalls);

        var completed = await engine.SubmitHumanApprovalAsync(submission);
        Assert.True(completed.Accepted);
        Assert.Equal(WorkflowStatus.Passed, completed.WorkflowResult!.Status);
        Assert.Equal(1, downstreamCalls);
        Assert.False(engine.TryGetPendingHumanApproval(context.TaskId, out _));
        Assert.False((await engine.SubmitHumanApprovalAsync(submission)).Accepted);
        Assert.Equal(1, downstreamCalls);
    }

    [Theory]
    [InlineData(WorkflowApprovalDecision.Approve)]
    [InlineData(WorkflowApprovalDecision.Reject)]
    [InlineData(WorkflowApprovalDecision.RequestRevision)]
    public async Task TwoApprovalBoundariesRetainAllStepAttemptsAndOrderedAudit(WorkflowApprovalDecision secondDecision)
    {
        var store = new InMemoryWorkflowApprovalStore();
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(), new InMemoryAuditLog(), store);
        var context = Context();
        var prefixCalls = 0;
        var firstApprovalCalls = 0;
        var middleCalls = 0;
        var secondApprovalCalls = 0;
        var downstreamCalls = 0;
        var paused = await engine.ExecuteAsync(
        [
            new WorkflowStep("prefix", _ => Task.FromResult(Result("prefix",
                ++prefixCalls == 1 ? GateDecisionResult.Rejected : GateDecisionResult.Passed))),
            new WorkflowStep("first", _ =>
            {
                firstApprovalCalls++;
                return Task.FromResult(Result("first", GateDecisionResult.NeedsHumanApproval));
            }),
            new WorkflowStep("middle", _ =>
            {
                middleCalls++;
                return Task.FromResult(Result("middle", GateDecisionResult.Passed));
            }),
            new WorkflowStep("second", _ =>
            {
                secondApprovalCalls++;
                return Task.FromResult(Result("second", GateDecisionResult.NeedsHumanApproval));
            }),
            new WorkflowStep("downstream", _ =>
            {
                downstreamCalls++;
                return Task.FromResult(Result("downstream", GateDecisionResult.Passed));
            })
        ], context);

        // 两次提交之间的其他工作流不能混入此工作流的历史。
        await engine.ExecuteAsync([new WorkflowStep("unrelated", _ => Task.FromResult(Result("unrelated", GateDecisionResult.Passed)))], Context());
        var first = await engine.SubmitHumanApprovalAsync(Submission(engine, context.TaskId, WorkflowApprovalDecision.Approve, "reviewer-1"));
        Assert.True(first.Accepted);
        Assert.Equal(WorkflowStatus.WaitingForHumanApproval, first.WorkflowResult!.Status);
        Assert.Equal("second", first.WorkflowResult.HumanApprovalRequest!.StepId);
        Assert.Equal(new[] { "prefix", "prefix", "first", "middle", "second" }, first.WorkflowResult.Steps.Select(step => step.StepId));
        Assert.True(store.TryGet(context.TaskId, out var secondPending));
        Assert.Equal(first.WorkflowResult.Steps.Take(4), secondPending.CompletedSteps);
        Assert.Equal(first.WorkflowResult.AuditLogs, secondPending.AuditLogs);
        Assert.Equal(0, downstreamCalls);

        var secondSubmission = Submission(engine, context.TaskId, secondDecision, "reviewer-2");
        var second = await engine.SubmitHumanApprovalAsync(secondSubmission);
        Assert.True(second.Accepted);
        var result = second.WorkflowResult!;
        var approved = secondDecision == WorkflowApprovalDecision.Approve;
        Assert.Equal(approved ? WorkflowStatus.Passed : WorkflowStatus.Rejected, result.Status);
        var expectedIds = new List<string> { "prefix", "prefix", "first", "middle", "second" };
        if (approved) expectedIds.Add("downstream");
        Assert.Equal(expectedIds, result.Steps.Select(step => step.StepId));
        Assert.Equal(WorkflowStepStatus.Retrying, result.Steps[0].Status);
        Assert.Equal(WorkflowStepStatus.Passed, result.Steps[2].Status);
        Assert.Equal(approved ? WorkflowStepStatus.Passed : WorkflowStepStatus.Rejected, result.Steps[4].Status);
        Assert.Equal(2, prefixCalls);
        Assert.Equal(1, firstApprovalCalls);
        Assert.Equal(1, middleCalls);
        Assert.Equal(1, secondApprovalCalls);
        Assert.Equal(approved ? 1 : 0, downstreamCalls);
        if (!approved) Assert.Equal("second", result.FailureReport!.FailedStepId);

        var expectedAudit = new List<string>
        {
            $"{context.TaskId}:workflow_started",
            "prefix:workflow_step_retrying",
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
        Assert.Equal(expectedAudit, result.AuditLogs.Select(entry => $"{entry.Actor}:{entry.Action}"));
        Assert.Contains(result.AuditLogs, entry => entry.Action == "workflow_human_approval_submitted" &&
                                                   entry.Message.Contains($"{secondDecision} from reviewer-2", StringComparison.Ordinal));
        Assert.Equal(WorkflowStepStatus.WaitingForHumanApproval, paused.Steps.Last().Status);
        Assert.Equal(WorkflowStepStatus.WaitingForHumanApproval, first.WorkflowResult.Steps.Last().Status);
        Assert.False(engine.TryGetPendingHumanApproval(context.TaskId, out _));
        Assert.False((await engine.SubmitHumanApprovalAsync(secondSubmission)).Accepted);
        Assert.Equal(approved ? 1 : 0, downstreamCalls);
    }

    [Fact]
    public async Task FinalApprovalWithoutRemainingStepsIncludesSubmissionDecisionAndCompletionAudit()
    {
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy());
        var context = Context();
        await engine.ExecuteAsync([new WorkflowStep("last", _ => Task.FromResult(Result("last", GateDecisionResult.NeedsHumanApproval)))], context);

        var approved = await engine.SubmitHumanApprovalAsync(Submission(engine, context.TaskId, WorkflowApprovalDecision.Approve, "reviewer"));

        Assert.True(approved.Accepted);
        Assert.Equal(WorkflowStatus.Passed, approved.WorkflowResult!.Status);
        Assert.Equal(WorkflowStepStatus.Passed, Assert.Single(approved.WorkflowResult.Steps).Status);
        Assert.Equal(new[]
        {
            "workflow_started", "workflow_step_waiting_for_human_approval",
            "workflow_human_approval_submitted", "workflow_human_approval_approved", "workflow_passed"
        }, approved.WorkflowResult.AuditLogs.Select(entry => entry.Action));
    }

    [Fact]
    public async Task ConcurrentDuplicateSubmissionCannotExecuteDownstreamTwice()
    {
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy());
        var context = Context();
        var downstreamCalls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await engine.ExecuteAsync(
        [
            new WorkflowStep("approval", _ => Task.FromResult(Result("approval", GateDecisionResult.NeedsHumanApproval))),
            new WorkflowStep("downstream", async _ =>
            {
                Interlocked.Increment(ref downstreamCalls);
                started.TrySetResult();
                await release.Task;
                return Result("downstream", GateDecisionResult.Passed);
            })
        ], context);
        var submission = Submission(engine, context.TaskId, WorkflowApprovalDecision.Approve, "reviewer");
        var first = engine.SubmitHumanApprovalAsync(submission);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False((await engine.SubmitHumanApprovalAsync(submission)).Accepted);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.True((await first).Accepted);
        Assert.Equal(1, downstreamCalls);
    }

    [Fact]
    public async Task ReliabilitySelfCheckExecutesAllThreeBehaviorScenarios()
    {
        var checks = await WorkflowReliabilitySelfCheck.RunAsync();
        Assert.Equal(3, checks.Count);
        Assert.All(checks, check => Assert.True(check.Value, check.Key));
    }

    private static WorkflowApprovalSubmission Submission(SequentialWorkflowEngine engine, string workflowId,
        WorkflowApprovalDecision decision, string submittedBy)
    {
        Assert.True(engine.TryGetPendingHumanApproval(workflowId, out var request));
        return new(workflowId, decision, submittedBy, ApprovalRequestId: request.ApprovalRequestId, StepId: request.StepId);
    }

    private static WorkflowContext Context() => new($"workflow-reliability-{Guid.NewGuid():N}", new Dictionary<string, object?>());

    private static WorkflowStepResult Result(string stepId, GateDecisionResult decision, params string[] issues) =>
        new(stepId, stepId, WorkflowStepStatus.Running, $"{stepId}: {decision}",
            GateDecision: new GateDecision($"gate-{stepId}", decision, $"{stepId}: {decision}"),
            Issues: issues, Logs: [$"executed:{stepId}"]);

    private sealed class CountingApprovalStore : IWorkflowApprovalStore
    {
        private readonly InMemoryWorkflowApprovalStore _inner = new();
        public int TakeCalls { get; private set; }
        public void Save(PendingWorkflowApproval pending) => _inner.Save(pending);
        public bool TryGet(string workflowId, out PendingWorkflowApproval pending) => _inner.TryGet(workflowId, out pending);
        public bool TryTake(string workflowId, string approvalRequestId, string stepId, out PendingWorkflowApproval pending)
        {
            TakeCalls++;
            return _inner.TryTake(workflowId, approvalRequestId, stepId, out pending);
        }
    }
}
