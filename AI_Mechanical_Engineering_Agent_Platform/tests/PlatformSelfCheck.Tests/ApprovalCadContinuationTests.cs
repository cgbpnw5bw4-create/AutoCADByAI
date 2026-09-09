using System.Text.Json;
using System.Text.Json.Serialization;
using AgentGatewayHost;
using DomainSchemas;
using PlatformCore;
using SolidWorksWorker;
using WorkerContracts;

namespace PlatformSelfCheck.Tests;

public sealed class ApprovalCadContinuationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovalRunsCadOnceAndArtifactGateDeterminesTaskResult(bool brokenArtifact)
    {
        using var fixture = new Fixture(brokenArtifact);
        var response = (await fixture.Dispatcher.DispatchAsync("chief-engineer", fixture.Request))!;
        Assert.Equal(PlatformTaskStatus.WaitingForHumanApproval, response.TaskStatus);
        Assert.Equal(0, fixture.Worker.Calls);
        var approval = ToApproval(response.PendingApproval!);
        var result = await fixture.Dispatcher.SubmitApprovalAsync(response.TaskId!, response.TaskAccessToken, approval);
        Assert.True(result.Accepted);
        Assert.Equal(brokenArtifact ? PlatformTaskStatus.Failed : PlatformTaskStatus.Passed, result.Task!.Task.Status);
        Assert.Equal(1, fixture.Worker.Calls);
        Assert.True(fixture.Worker.LastRequest!.DryRun);
        Assert.Equal(response.TaskId, result.Task.Task.Id);
        Assert.Contains(fixture.Platform.AuditLog.GetEntries(), entry => entry.Action == "quality_gate_after_solidworks_main_workflow");
        Assert.Contains("SolidWorks main CAD workflow status", result.Task.Result!.CollaborationReport!.Summary);
        Assert.False((await fixture.Dispatcher.SubmitApprovalAsync(response.TaskId!, response.TaskAccessToken, approval)).Accepted);
        Assert.Equal(1, fixture.Worker.Calls);
    }

    [Theory]
    [InlineData(WorkflowApprovalDecision.Reject)]
    [InlineData(WorkflowApprovalDecision.RequestRevision)]
    public async Task DeclinedApprovalStopsBeforeCad(WorkflowApprovalDecision decision)
    {
        using var fixture = new Fixture(false);
        var response = (await fixture.Dispatcher.DispatchAsync("chief-engineer", fixture.Request))!;
        var result = await fixture.Dispatcher.SubmitApprovalAsync(response.TaskId!, response.TaskAccessToken,
            ToApproval(response.PendingApproval!) with { Decision = decision });
        Assert.True(result.Accepted);
        Assert.Equal(PlatformTaskStatus.Rejected, result.Task!.Task.Status);
        Assert.Equal(0, fixture.Worker.Calls);
        Assert.Null(result.Task.PendingApproval);
        Assert.DoesNotContain(fixture.Platform.AuditLog.GetEntries(), entry => entry.Action == "solidworks_main_workflow_invoked");
    }

    [Fact]
    public async Task OriginalInputRemainsBoundAfterCallerMutatesRequestDictionary()
    {
        using var fixture = new Fixture(false);
        var response = (await fixture.Dispatcher.DispatchAsync("chief-engineer", fixture.Request))!;
        fixture.Values["dry_run"] = "false";
        fixture.Values["cad_model_spec_json"] = "{";
        fixture.Values["solidworks_output_directory"] = "invalid-updated-directory";
        var approval = ToApproval(response.PendingApproval!);
        var result = await fixture.Dispatcher.SubmitApprovalAsync(response.TaskId!, response.TaskAccessToken, approval);
        Assert.Equal(PlatformTaskStatus.Passed, result.Task!.Task.Status);
        Assert.True(fixture.Worker.LastRequest!.DryRun);
        Assert.StartsWith(fixture.Root, fixture.Worker.LastRequest.OutputDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.False((await fixture.Dispatcher.SubmitApprovalAsync(response.TaskId!, response.TaskAccessToken, approval)).Accepted);
        Assert.Equal(1, fixture.Worker.Calls);
    }

    [Fact]
    public async Task JsonWithOmittedDecisionCannotApprove()
    {
        using var fixture = new Fixture(false);
        var response = (await fixture.Dispatcher.DispatchAsync("chief-engineer", fixture.Request))!;
        var pending = response.PendingApproval!;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(new { pending.WorkflowId, pending.ApprovalRequestId, pending.StepId, submitted_by = "reviewer" }, options);
        var approval = JsonSerializer.Deserialize<GatewayApprovalRequest>(json, options)!;
        Assert.Null(approval.Decision);
        var result = await fixture.Dispatcher.SubmitApprovalAsync(response.TaskId!, response.TaskAccessToken, approval);
        Assert.False(result.Accepted);
        Assert.Equal(pending.ApprovalRequestId, fixture.Dispatcher.GetTask(response.TaskId!, response.TaskAccessToken)!.PendingApproval!.ApprovalRequestId);
        Assert.Equal(0, fixture.Worker.Calls);
    }

    [Fact]
    public async Task TaskLifecycleSelfCheckExercisesRoundtripWithoutCad()
    {
        var checks = await TaskLifecycleSelfCheck.RunAsync(PlatformPathResolver.FindProjectRoot());
        Assert.Equal(3, checks.Count);
        Assert.All(checks, check => Assert.True(check.Value, check.Key));
    }

    private static GatewayApprovalRequest ToApproval(HumanApprovalRequest pending) =>
        new(pending.WorkflowId, pending.ApprovalRequestId!, pending.StepId, WorkflowApprovalDecision.Approve, "test-reviewer");

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "approval-cad-" + Guid.NewGuid().ToString("N"));
        public PlatformKernel Platform { get; } = PlatformBootstrapper.CreateDefault(PlatformPathResolver.FindProjectRoot());
        public CountingWorker Worker { get; }
        public AgentMessageDispatcher Dispatcher { get; }
        public Dictionary<string, string> Values { get; }
        public GatewayMessageRequest Request { get; }
        public Fixture(bool brokenArtifact)
        {
            Worker = new CountingWorker(brokenArtifact);
            Platform.WorkerRegistry.Register(Worker);
            Dispatcher = new AgentMessageDispatcher(Platform);
            Values = new()
            {
                ["test_scenario"] = "drawing_reviewer_needs_human_approval", ["dry_run"] = "true",
                ["solidworks_main_workflow"] = "true", ["solidworks_output_directory"] = Root,
                ["project_root"] = PlatformPathResolver.FindProjectRoot()
            };
            Request = new("test", "local", Guid.NewGuid().ToString("N"), "tester", "请求审批建模", [], Values);
        }
        public void Dispose()
        {
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("临时目录越界。");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed class CountingWorker(bool brokenArtifact) : ISolidWorksWorker
    {
        private readonly FakeSolidWorksWorker _fake = new();
        public int Calls { get; private set; }
        public SolidWorksWorkerRequest? LastRequest { get; private set; }
        public string Name => "FakeSolidWorksWorker";
        public string TargetSystem => "SolidWorks";
        public async Task<SolidWorksWorkerResult> ExecuteAsync(SolidWorksWorkerRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            var result = await _fake.ExecuteAsync(request, cancellationToken);
            // 模拟 Worker 声称成功但未交付产物，必须由后续产物门禁拒绝。
            return brokenArtifact ? result with { GeneratedArtifacts = [] } : result;
        }
        public Task<WorkerOutput> ExecuteAsync(WorkerInput input) => throw new InvalidOperationException("必须使用受控 CAD 请求。");
    }
}
