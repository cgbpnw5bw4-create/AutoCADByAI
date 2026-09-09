using AgentContracts;
using AgentGatewayHost;
using AgentRuntime.Microsoft;
using DomainSchemas;
using PlatformCore;
using PlatformCore.Modules.RequirementUnderstanding.Agents;
using QualityGate;

namespace PlatformSelfCheck.Tests;

public sealed class GatewayTaskLifecycleTests
{
    [Theory]
    [InlineData(AgentOutputStatus.Completed, PlatformTaskStatus.Passed, "completed")]
    [InlineData(AgentOutputStatus.Failed, PlatformTaskStatus.Failed, "failed")]
    [InlineData(AgentOutputStatus.Rejected, PlatformTaskStatus.Rejected, "rejected")]
    public async Task TaskStatusAndQueryableResultFollowFinalQualityDecision(
        AgentOutputStatus outputStatus, PlatformTaskStatus taskStatus, string responseStatus)
    {
        var fixture = new Fixture(Agent("planner", outputStatus));
        var response = await fixture.Start();
        Assert.Equal(taskStatus, response.TaskStatus);
        Assert.Equal(responseStatus, response.Status);
        Assert.Equal(64, response.TaskAccessToken!.Length);
        var task = fixture.Gateway.GetTask(response.TaskId!, response.TaskAccessToken);
        Assert.NotNull(task);
        Assert.Equal(taskStatus, task.Task.Status);
        Assert.Equal(responseStatus, task.Result!.Status);
        Assert.Null(task.Result.TaskAccessToken);
        Assert.Null(task.PendingApproval);
        Assert.Equal(response.TaskId, Assert.Single(fixture.Platform.TaskStore.GetAll()).Id);
    }

    [Theory]
    [InlineData("unknown-agent")]
    [InlineData("planner")]
    public async Task UnknownOrInternalAgentDoesNotCreatePublicTask(string agentId)
    {
        var agent = Agent("planner", AgentOutputStatus.Completed);
        var fixture = new Fixture(agent);
        Assert.Null(await fixture.Gateway.DispatchAsync(agentId, Request()));
        Assert.Empty(fixture.Platform.TaskStore.GetAll());
        Assert.Equal(0, agent.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-token")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task MissingOrWrongTokenCannotReadOrConsumeApproval(string? token)
    {
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval));
        var paused = await fixture.Start();
        Assert.Null(fixture.Gateway.GetTask(paused.TaskId!, token));
        var denied = await fixture.Gateway.SubmitApprovalAsync(paused.TaskId!, token, Approval(paused.PendingApproval!));
        Assert.False(denied.Accepted);
        Assert.True(denied.NotFound);
        Assert.Null(denied.Task);
        var retained = fixture.Gateway.GetTask(paused.TaskId!, paused.TaskAccessToken)!;
        Assert.Equal(PlatformTaskStatus.WaitingForHumanApproval, retained.Task.Status);
        Assert.Equal(paused.PendingApproval, retained.PendingApproval);
        Assert.True((await fixture.Approve(paused)).Accepted);
    }

    [Fact]
    public async Task ApprovalAtFinalStepProducesPassedTaskAndPreservesOriginalEvidence()
    {
        var original = Output(AgentOutputStatus.NeedsHumanApproval);
        var reviewer = new StubAgent("review", _ => Task.FromResult(original));
        var fixture = new Fixture(reviewer);
        var paused = await fixture.Start();
        Assert.Equal(PlatformTaskStatus.WaitingForHumanApproval, paused.TaskStatus);
        Assert.Equal("needs_human_approval", paused.Status);
        Assert.NotNull(paused.PendingApproval!.ApprovalRequestId);
        var oldSnapshot = fixture.Gateway.GetTask(paused.TaskId!, paused.TaskAccessToken)!;

        var approved = await fixture.Approve(paused);
        Assert.True(approved.Accepted);
        Assert.Equal(PlatformTaskStatus.Passed, approved.Task!.Task.Status);
        Assert.Equal(GateDecisionResult.Passed, approved.Task.Result!.GateDecision.Result);
        Assert.Equal("completed", approved.Task.Result.Status);
        Assert.Null(approved.Task.PendingApproval);
        Assert.Empty(approved.Task.Result.Issues);
        var report = approved.Task.Result.CollaborationReport!;
        Assert.Equal("Completed", Assert.Single(report.CalledAgents).Status);
        Assert.True(Assert.Single(report.AgentOutputs).ReviewReport!.IsPassed);
        var resolution = Assert.Single(report.StepResults!).ApprovalResolution!;
        Assert.Equal(paused.PendingApproval.ApprovalRequestId, resolution.ApprovalRequestId);
        Assert.Equal("Approve", resolution.Decision);
        Assert.Equal(original.ReviewReport, resolution.OriginalReviewReport);
        Assert.Contains(original.Issues[0], resolution.OriginalIssues);
        Assert.Equal(PlatformTaskStatus.WaitingForHumanApproval, oldSnapshot.Task.Status);
        Assert.Equal(paused.PendingApproval, oldSnapshot.PendingApproval);
        Assert.Equal(1, reviewer.Calls);
    }

    [Theory]
    [InlineData(WorkflowApprovalDecision.Reject)]
    [InlineData(WorkflowApprovalDecision.RequestRevision)]
    public async Task RejectionAndRevisionBecomeTerminalRejectedWithoutRunningDownstream(WorkflowApprovalDecision decision)
    {
        var downstream = Agent("downstream", AgentOutputStatus.Completed);
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval), downstream);
        var paused = await fixture.Start();
        var rejected = await fixture.Gateway.SubmitApprovalAsync(paused.TaskId!, paused.TaskAccessToken,
            Approval(paused.PendingApproval!, decision));
        Assert.True(rejected.Accepted);
        Assert.Equal(PlatformTaskStatus.Rejected, rejected.Task!.Task.Status);
        Assert.Equal("rejected", rejected.Task.Result!.Status);
        Assert.Equal(GateDecisionResult.Rejected, rejected.Task.Result.GateDecision.Result);
        Assert.Null(rejected.Task.PendingApproval);
        Assert.NotEqual("task_approval_continuation_missing", rejected.Task.FailureStage);
        Assert.Equal(decision.ToString(), Assert.Single(rejected.Task.Result.CollaborationReport!.StepResults!).ApprovalResolution!.Decision);
        Assert.Equal(0, downstream.Calls);
        Assert.False((await fixture.Approve(paused)).Accepted);
    }

    [Fact]
    public async Task TwoApprovalsRejectStaleIdentityAndKeepEarlierResolution()
    {
        var downstream = Agent("downstream", AgentOutputStatus.Completed);
        var fixture = new Fixture(Agent("first", AgentOutputStatus.NeedsHumanApproval), Agent("second", AgentOutputStatus.NeedsHumanApproval), downstream);
        var first = await fixture.Start();
        var advanced = await fixture.Approve(first);
        var second = advanced.Task!.PendingApproval!;
        Assert.Equal(PlatformTaskStatus.WaitingForHumanApproval, advanced.Task.Task.Status);
        Assert.NotEqual(first.PendingApproval!.ApprovalRequestId, second.ApprovalRequestId);
        Assert.False((await fixture.Approve(first)).Accepted);
        Assert.Equal(second, fixture.Gateway.GetTask(first.TaskId!, first.TaskAccessToken)!.PendingApproval);
        Assert.Equal(0, downstream.Calls);
        var completed = await fixture.Gateway.SubmitApprovalAsync(first.TaskId!, first.TaskAccessToken, Approval(second));
        Assert.Equal(PlatformTaskStatus.Passed, completed.Task!.Task.Status);
        var steps = completed.Task.Result!.CollaborationReport!.StepResults!;
        Assert.Equal(first.PendingApproval.ApprovalRequestId, steps[0].ApprovalResolution!.ApprovalRequestId);
        Assert.Equal(second.ApprovalRequestId, steps[1].ApprovalResolution!.ApprovalRequestId);
        Assert.Equal(1, downstream.Calls);
    }

    [Fact]
    public async Task TokenAndApprovalIdentityCannotBeReusedAcrossTasks()
    {
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval));
        var first = await fixture.Start();
        var second = await fixture.Start();
        Assert.NotEqual(first.TaskId, second.TaskId);
        Assert.NotEqual(first.TaskAccessToken, second.TaskAccessToken);
        Assert.Null(fixture.Gateway.GetTask(second.TaskId!, first.TaskAccessToken));
        var tokenDenied = await fixture.Gateway.SubmitApprovalAsync(second.TaskId!, first.TaskAccessToken, Approval(second.PendingApproval!));
        Assert.True(tokenDenied.NotFound);
        var identityDenied = await fixture.Gateway.SubmitApprovalAsync(second.TaskId!, second.TaskAccessToken, Approval(first.PendingApproval!));
        Assert.False(identityDenied.Accepted);
        Assert.False(identityDenied.NotFound);
        Assert.Equal(second.PendingApproval, fixture.Gateway.GetTask(second.TaskId!, second.TaskAccessToken)!.PendingApproval);
        Assert.True((await fixture.Approve(first)).Accepted);
        Assert.True((await fixture.Approve(second)).Accepted);
    }

    [Fact]
    public async Task ConcurrentDuplicateApprovalExecutesDownstreamOnce()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downstream = new StubAgent("downstream", async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return Output(AgentOutputStatus.Completed);
        });
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval), downstream);
        var paused = await fixture.Start();
        var first = fixture.Approve(paused);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal(PlatformTaskStatus.Running, fixture.Gateway.GetTask(paused.TaskId!, paused.TaskAccessToken)!.Task.Status);
            var duplicate = await fixture.Approve(paused);
            Assert.False(duplicate.Accepted);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(PlatformTaskStatus.Passed, (await first).Task!.Task.Status);
        Assert.False((await fixture.Approve(paused)).Accepted);
        Assert.Equal(1, downstream.Calls);
    }

    [Fact]
    public async Task PreCancelledApprovalPreservesWaitingTaskAndCanBeSubmittedNormally()
    {
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval));
        var paused = await fixture.Start();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Gateway.SubmitApprovalAsync(paused.TaskId!,
            paused.TaskAccessToken, Approval(paused.PendingApproval!), new CancellationToken(canceled: true)));
        var retained = fixture.Gateway.GetTask(paused.TaskId!, paused.TaskAccessToken)!;
        Assert.Equal(PlatformTaskStatus.WaitingForHumanApproval, retained.Task.Status);
        Assert.Equal(paused.PendingApproval, retained.PendingApproval);
        Assert.Equal(PlatformTaskStatus.Passed, (await fixture.Approve(paused)).Task!.Task.Status);
    }

    [Fact]
    public async Task CallerCancellationAfterApprovalAcceptanceDoesNotCancelBusinessContinuation()
    {
        using var requestAborted = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downstream = new StubAgent("downstream", async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return Output(AgentOutputStatus.Completed);
        });
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval), downstream);
        var paused = await fixture.Start();
        var accepted = fixture.Gateway.SubmitApprovalAsync(paused.TaskId!, paused.TaskAccessToken,
            Approval(paused.PendingApproval!), requestAborted.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            // 模拟已受理请求的 RequestAborted；此用例不执行真实 TCP 断开。
            requestAborted.Cancel();
            Assert.Equal(PlatformTaskStatus.Running, fixture.Gateway.GetTask(paused.TaskId!, paused.TaskAccessToken)!.Task.Status);
        }
        finally { release.TrySetResult(); }
        var completed = await accepted;
        Assert.True(completed.Accepted);
        Assert.Equal(PlatformTaskStatus.Passed, completed.Task!.Task.Status);
        Assert.Equal(PlatformTaskStatus.Passed, fixture.Gateway.GetTask(paused.TaskId!, paused.TaskAccessToken)!.Task.Status);
        Assert.False((await fixture.Approve(paused)).Accepted);
        Assert.Equal(1, downstream.Calls);
    }

    [Fact]
    public async Task InternalExceptionAfterApprovalCreatesFailedTaskAndCannotReplay()
    {
        var downstream = new StubAgent("downstream", _ => throw new InvalidOperationException("downstream failed"));
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval), downstream);
        var paused = await fixture.Start();
        var completed = await fixture.Approve(paused);
        Assert.True(completed.Accepted);
        Assert.Equal(PlatformTaskStatus.Failed, completed.Task!.Task.Status);
        Assert.Contains(completed.Task.Result!.Issues, issue => issue.Contains("exception", StringComparison.OrdinalIgnoreCase));
        Assert.Null(completed.Task.PendingApproval);
        Assert.False((await fixture.Approve(paused)).Accepted);
        Assert.Equal(1, downstream.Calls);
    }

    [Fact]
    public async Task PublicInitialExceptionIsQueryableAsFailedTask()
    {
        var platform = new PlatformKernel();
        platform.AgentRegistry.Register(new StubAgent("chief-engineer", _ => throw new InvalidOperationException("initial failed"), AgentVisibility.Public));
        var gateway = new AgentMessageDispatcher(platform);
        var response = (await gateway.DispatchAsync("chief-engineer", Request()))!;
        Assert.Equal(PlatformTaskStatus.Failed, response.TaskStatus);
        Assert.Equal("task_execution_exception", response.FailureStage);
        Assert.Equal(PlatformTaskStatus.Failed, gateway.GetTask(response.TaskId!, response.TaskAccessToken)!.Task.Status);
        Assert.Contains(response.Issues, issue => issue.Contains("initial failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublicResumeExceptionIsTerminalAndCannotReplayAcceptedApproval()
    {
        var platform = new PlatformKernel();
        var agent = new ThrowingResumeAgent();
        platform.AgentRegistry.Register(agent);
        var gateway = new AgentMessageDispatcher(platform);
        var paused = (await gateway.DispatchAsync("chief-engineer", Request()))!;
        var completed = await gateway.SubmitApprovalAsync(paused.TaskId!, paused.TaskAccessToken, Approval(paused.PendingApproval!));
        Assert.True(completed.Accepted);
        Assert.Equal(PlatformTaskStatus.Failed, completed.Task!.Task.Status);
        Assert.Equal("task_execution_exception", completed.Task.FailureStage);
        Assert.Null(completed.Task.PendingApproval);
        Assert.False((await gateway.SubmitApprovalAsync(paused.TaskId!, paused.TaskAccessToken, Approval(paused.PendingApproval!))).Accepted);
        Assert.Equal(1, agent.ResumeCalls);
    }

    [Fact]
    public async Task ResumeExceptionPreservesPreviouslyObservedMicrosoftMetadata()
    {
        var platform = new PlatformKernel();
        var agent = new ThrowingResumeAgent(Metadata);
        platform.AgentRegistry.Register(agent);
        var gateway = new AgentMessageDispatcher(platform);
        var paused = (await gateway.DispatchAsync("chief-engineer", Request()))!;
        Assert.Equal("Microsoft", paused.RuntimeMode);
        var completed = await gateway.SubmitApprovalAsync(paused.TaskId!, paused.TaskAccessToken, Approval(paused.PendingApproval!));
        Assert.True(completed.Accepted);
        Assert.Equal(PlatformTaskStatus.Failed, completed.Task!.Task.Status);
        Assert.Equal("task_execution_exception", completed.Task.FailureStage);
        var result = completed.Task.Result!;
        Assert.Equal("Microsoft", result.RuntimeMode);
        Assert.Equal(Metadata.RuntimeProvider, result.RuntimeProvider);
        Assert.Equal(Metadata.RuntimeModel, result.RuntimeModel);
        Assert.Equal(Metadata.ChiefEngineerRuntimeUsed, result.ChiefEngineerRuntimeUsed);
        var retained = gateway.GetTask(paused.TaskId!, paused.TaskAccessToken)!.Result!;
        Assert.Equal(result.RuntimeMode, retained.RuntimeMode);
        Assert.Equal(result.RuntimeProvider, retained.RuntimeProvider);
        Assert.Equal(result.RuntimeModel, retained.RuntimeModel);
        Assert.Equal(1, agent.ResumeCalls);
    }

    [Theory]
    [InlineData(AgentRuntimeMode.Mock)]
    [InlineData(AgentRuntimeMode.Microsoft)]
    public async Task RuntimeAdapterResumesWithoutRepeatingModelInvocation(AgentRuntimeMode mode)
    {
        var invoker = new CountingInvoker(Output(AgentOutputStatus.Completed) with { RuntimeMetadata = Metadata });
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval));
        fixture.WrapRuntime(mode, invoker);
        var paused = await fixture.Start();
        var completed = await fixture.Approve(paused);
        Assert.Equal(PlatformTaskStatus.Passed, completed.Task!.Task.Status);
        Assert.Equal(mode == AgentRuntimeMode.Microsoft ? 1 : 0, invoker.Calls);
        Assert.Equal(mode.ToString(), completed.Task.Result!.RuntimeMode);
        if (mode == AgentRuntimeMode.Microsoft)
        {
            Assert.Equal(Metadata.RuntimeProvider, completed.Task.Result.RuntimeProvider);
            Assert.Equal(Metadata.RuntimeModel, completed.Task.Result.RuntimeModel);
            Assert.True(completed.Task.Result.ChiefEngineerRuntimeUsed);
        }
    }

    [Fact]
    public async Task UnresolvedRuntimeIssuesSurviveApprovalAndKeepFinalTaskRejected()
    {
        const string unresolved = "runtime advisory requires engineering evidence";
        var invoker = new CountingInvoker(Output(AgentOutputStatus.Completed) with
        {
            Issues = [unresolved], RuntimeMetadata = Metadata
        });
        var fixture = new Fixture(Agent("review", AgentOutputStatus.NeedsHumanApproval));
        fixture.WrapRuntime(AgentRuntimeMode.Microsoft, invoker);
        var paused = await fixture.Start();
        var completed = await fixture.Approve(paused);
        Assert.Equal(PlatformTaskStatus.Rejected, completed.Task!.Task.Status);
        Assert.Contains(unresolved, completed.Task.Result!.Issues);
        Assert.Equal(Metadata.RuntimeProvider, completed.Task.Result.RuntimeProvider);
        Assert.Equal(Metadata.RuntimeModel, completed.Task.Result.RuntimeModel);
        Assert.Equal(1, invoker.Calls);
        Assert.Null(completed.Task.PendingApproval);
    }

    [Fact]
    public async Task RuntimeAdvisorySurvivesTwoPausesAndRejectedReplay()
    {
        var invoker = new CountingInvoker(Output(AgentOutputStatus.Completed) with { RuntimeMetadata = Metadata });
        var fixture = new Fixture(Agent("first", AgentOutputStatus.NeedsHumanApproval), Agent("second", AgentOutputStatus.NeedsHumanApproval));
        fixture.WrapRuntime(AgentRuntimeMode.Microsoft, invoker);
        var paused = await fixture.Start();
        var second = await fixture.Approve(paused);
        Assert.Equal(Metadata.RuntimeModel, second.Task!.Result!.RuntimeModel);
        Assert.False((await fixture.Approve(paused)).Accepted);
        var completed = await fixture.Gateway.SubmitApprovalAsync(paused.TaskId!, paused.TaskAccessToken, Approval(second.Task.PendingApproval!));
        Assert.Equal(PlatformTaskStatus.Passed, completed.Task!.Task.Status);
        Assert.Equal(Metadata.RuntimeModel, completed.Task.Result!.RuntimeModel);
        Assert.Equal(1, invoker.Calls);
    }

    [Fact]
    public async Task RunningTaskIsObservableAndFinalSnapshotDoesNotRewriteEarlierSnapshot()
    {
        var platform = new PlatformKernel();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        platform.AgentRegistry.Register(new StubAgent("chief-engineer", async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return Output(AgentOutputStatus.Completed);
        }, AgentVisibility.Public));
        var service = new AgentTaskService(platform);
        var execution = service.ExecuteAsync("chief-engineer", Input());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var running = Assert.Single(platform.TaskStore.GetAll());
        try { Assert.Equal(PlatformTaskStatus.Running, running.Status); }
        finally { release.TrySetResult(); }
        var completed = (await execution)!;
        Assert.Equal(PlatformTaskStatus.Passed, completed.Result.Task.Status);
        Assert.Equal(PlatformTaskStatus.Running, running.Status);
        Assert.Equal(running.CreatedAt, completed.Result.Task.CreatedAt);
        Assert.True(completed.Result.Task.UpdatedAt >= running.UpdatedAt);
        Assert.NotNull(service.Get(completed.Result.Task.Id, completed.AccessToken));
    }

    [Fact]
    public async Task PreCancelledDispatchCreatesNoTaskAndDoesNotExecuteAgent()
    {
        var agent = Agent("planner", AgentOutputStatus.Completed);
        var fixture = new Fixture(agent);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Gateway.DispatchAsync("chief-engineer", Request(),
            new CancellationToken(canceled: true)));
        Assert.Empty(fixture.Platform.TaskStore.GetAll());
        Assert.Equal(0, agent.Calls);
    }

    [Fact]
    public async Task WaitingOutputWithoutResumeContractFailsWithActionableStage()
    {
        var platform = new PlatformKernel();
        platform.AgentRegistry.Register(new StubAgent("chief-engineer", _ => Task.FromResult(Output(AgentOutputStatus.NeedsHumanApproval)), AgentVisibility.Public));
        var gateway = new AgentMessageDispatcher(platform);
        var response = (await gateway.DispatchAsync("chief-engineer", Request()))!;
        Assert.Equal(PlatformTaskStatus.Failed, response.TaskStatus);
        Assert.Equal("task_approval_continuation_missing", response.FailureStage);
        Assert.Null(response.PendingApproval);
    }

    private static readonly RuntimeMetadata Metadata = new("Microsoft", "test-provider", "test-model", false, null, true);
    private static StubAgent Agent(string id, AgentOutputStatus status) => new(id, _ => Task.FromResult(Output(status)));
    private static AgentOutput Output(AgentOutputStatus status)
    {
        string[] issues = status == AgentOutputStatus.NeedsHumanApproval ? ["needs_human_approval: drawing confirmation"] :
            status == AgentOutputStatus.Rejected ? ["quality evidence missing"] : [];
        var review = new ReviewReport("original-review", "original-reviewer", status == AgentOutputStatus.Completed,
            status == AgentOutputStatus.Completed ? 1.0 : 0.4, issues,
            status == AgentOutputStatus.NeedsHumanApproval, status == AgentOutputStatus.Failed);
        return new(status, "test output", [], issues, [], null, ReviewReport: review);
    }
    private static GatewayMessageRequest Request() => new("test", "test", Guid.NewGuid().ToString("N"), "tester", "检查规划流程。", [],
        new Dictionary<string, string> { ["dry_run"] = "true" });
    private static AgentInput Input() => new("test", "test", Guid.NewGuid().ToString("N"), "tester", "检查规划流程。", [],
        new Dictionary<string, string> { ["dry_run"] = "true" });
    private static GatewayApprovalRequest Approval(HumanApprovalRequest request, WorkflowApprovalDecision decision = WorkflowApprovalDecision.Approve) =>
        new(request.WorkflowId, request.ApprovalRequestId!, request.StepId, decision, "reviewer", "已审阅。");

    private sealed class Fixture
    {
        public PlatformKernel Platform { get; } = new();
        public AgentMessageDispatcher Gateway { get; }
        public Fixture(params StubAgent[] agents)
        {
            foreach (var agent in agents) Platform.AgentRegistry.Register(agent);
            var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(0), Platform.AuditLog);
            var orchestrator = new ChiefEngineerOrchestrator(new InternalAgentRouter(Platform.AgentRegistry, Platform.AuditLog),
                Platform.AgentRegistry, Platform.AuditLog, engine,
                internalWorkflowRoute: new InternalWorkflowRoute(agents.Select(agent => agent.Id).ToArray()));
            Platform.AgentRegistry.Register(new ChiefEngineerAgent(orchestrator));
            Gateway = new AgentMessageDispatcher(Platform);
        }
        public void WrapRuntime(AgentRuntimeMode mode, CountingInvoker invoker)
        {
            var chief = Platform.AgentRegistry.GetById("chief-engineer")!;
            Platform.AgentRegistry.Register(new MicrosoftAgentAdapter(chief, RuntimeAgentManifest.Create(chief.Id), mode, Platform.AuditLog, invoker));
        }
        public async Task<GatewayMessageResponse> Start() => (await Gateway.DispatchAsync("chief-engineer", Request()))!;
        public Task<GatewayApprovalResponse> Approve(GatewayMessageResponse paused) =>
            Gateway.SubmitApprovalAsync(paused.TaskId!, paused.TaskAccessToken, Approval(paused.PendingApproval!));
    }

    private sealed class StubAgent(string id, Func<AgentContext, Task<AgentOutput>> execute, AgentVisibility visibility = AgentVisibility.Internal) : IAgent
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public string Id => id;
        public string Name => id;
        public AgentRole Role => new(id, id, "test role");
        public string Description => "test agent";
        public AgentVisibility Visibility => visibility;
        public Task<AgentOutput> ExecuteAsync(AgentContext context) { Interlocked.Increment(ref _calls); return execute(context); }
    }

    private sealed class CountingInvoker(AgentOutput output) : IMicrosoftRuntimeAgentInvoker
    {
        public int Calls { get; private set; }
        public Task<AgentOutput> InvokeAsync(RuntimeAgentManifest manifest, AgentContext context, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(output); }
    }

    private sealed class ThrowingResumeAgent(RuntimeMetadata? metadata = null) : IAgent, IHumanApprovalAgent
    {
        public int ResumeCalls { get; private set; }
        public string Id => "chief-engineer";
        public string Name => Id;
        public AgentRole Role => new(Id, Id, "test role");
        public string Description => "test agent";
        public AgentVisibility Visibility => AgentVisibility.Public;
        public Task<AgentOutput> ExecuteAsync(AgentContext context)
        {
            var request = new HumanApprovalRequest($"stub-workflow-{context.TaskId}", "stub-step", "stub-step", Id,
                "needs_human_approval", ["approve", "reject"], "test", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"));
            return Task.FromResult(Output(AgentOutputStatus.NeedsHumanApproval) with
            {
                RuntimeMetadata = metadata,
                InternalCollaborationReport = new(context.Input.ConversationId, Id, [], [], [], [], "test", "test",
                    HumanApprovalRequest: request)
            });
        }
        public Task<AgentApprovalResult> ResumeHumanApprovalAsync(AgentContext context, WorkflowApprovalSubmission submission,
            CancellationToken cancellationToken = default)
        { ResumeCalls++; throw new InvalidOperationException("resume failed"); }
    }
}
