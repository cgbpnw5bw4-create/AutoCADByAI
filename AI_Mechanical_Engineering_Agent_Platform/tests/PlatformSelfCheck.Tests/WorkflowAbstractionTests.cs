using AgentContracts;
using AgentRuntime.Microsoft;
using DomainSchemas;
using PlatformCore;
using PlatformCore.Modules.RequirementUnderstanding.Agents;

namespace PlatformSelfCheck.Tests;

public sealed class WorkflowAbstractionTests
{
    [Fact]
    public async Task FrontierSelfCheckExercisesDefaultInputAndIndependentGateRejection()
    {
        var checks = await FrontierArchitectureSelfCheck.RunAsync(PlatformPathResolver.FindProjectRoot());
        Assert.Equal(4, checks.Count);
        Assert.All(checks, check => Assert.True(check.Value, check.Key));
    }

    [Fact]
    public async Task KernelOrchestratorAndRuntimeUseInjectedEngine()
    {
        var engine = new RecordingEngine();
        var kernel = new PlatformKernel(engine);
        var orchestrator = new ChiefEngineerOrchestrator(new InternalAgentRouter(kernel.AgentRegistry, kernel.AuditLog),
            kernel.AgentRegistry, kernel.AuditLog, kernel.WorkflowEngine);
        var context = new AgentContext("engine-substitution", new AgentInput("test", "test", "test", "test", "校验需求", [],
            new Dictionary<string, string>()), new Dictionary<string, object?>(), DateTimeOffset.UtcNow);
        var output = await orchestrator.ExecuteAsync(context, "chief-engineer", "机械总工程师");
        Assert.Same(engine, kernel.WorkflowEngine);
        Assert.Equal(1, engine.Calls);
        Assert.Equal(AgentOutputStatus.Failed, output.Status);

        var runtime = new MicrosoftWorkflowRuntime(engine);
        await runtime.ExecuteAsync([], new WorkflowContext("runtime", new Dictionary<string, object?>()));
        Assert.Equal(2, engine.Calls);
    }

    [Fact]
    public async Task InterfacePreservesSequentialRetryAndHumanApproval()
    {
        IWorkflowEngine engine = new SequentialWorkflowEngine(new QualityGate.DefaultRetryPolicy(1));
        var attempts = 0;
        var downstream = 0;
        var steps = new[]
        {
            new WorkflowStep("重试", _ => Task.FromResult(Step("retry", ++attempts == 1 ? GateDecisionResult.Rejected : GateDecisionResult.Passed)), "retry"),
            new WorkflowStep("审批", _ => Task.FromResult(Step("approval", GateDecisionResult.NeedsHumanApproval)), "approval"),
            new WorkflowStep("执行", _ => { downstream++; return Task.FromResult(Step("execute", GateDecisionResult.Passed)); }, "execute")
        };
        var paused = await engine.ExecuteAsync(steps, new WorkflowContext("contract", new Dictionary<string, object?>()));
        Assert.Equal(2, attempts);
        Assert.Equal(0, downstream);
        Assert.Equal(WorkflowStatus.WaitingForHumanApproval, paused.Status);
        Assert.True(engine.TryGetPendingHumanApproval("contract", out var pending));
        var submission = new WorkflowApprovalSubmission("contract", WorkflowApprovalDecision.Approve, "test",
            ApprovalRequestId: pending.ApprovalRequestId, StepId: pending.StepId);
        var resumed = await engine.SubmitHumanApprovalAsync(submission);
        Assert.True(resumed.Accepted);
        Assert.Equal(WorkflowStatus.Passed, resumed.WorkflowResult!.Status);
        Assert.Equal(1, downstream);
        Assert.False((await engine.SubmitHumanApprovalAsync(submission)).Accepted);
        Assert.Equal(2, attempts);
    }

    private static WorkflowStepResult Step(string id, GateDecisionResult decision) => new(id, id,
        decision switch
        {
            GateDecisionResult.Passed => WorkflowStepStatus.Passed,
            GateDecisionResult.Rejected => WorkflowStepStatus.Rejected,
            _ => WorkflowStepStatus.WaitingForHumanApproval
        }, id, GateDecision: new GateDecision(id, decision, id));

    private sealed class RecordingEngine : IWorkflowEngine
    {
        public int Calls { get; private set; }
        public Task<WorkflowExecutionResult> ExecuteAsync(IEnumerable<WorkflowStep> steps, WorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new WorkflowExecutionResult(context.TaskId, WorkflowStatus.Failed, [],
                new GateDecision("replacement", GateDecisionResult.Failed, "替换引擎阻断")));
        }
        public bool TryGetPendingHumanApproval(string workflowId, out HumanApprovalRequest request) { request = null!; return false; }
        public Task<WorkflowApprovalSubmissionResult> SubmitHumanApprovalAsync(WorkflowApprovalSubmission submission,
            CancellationToken cancellationToken = default) => Task.FromResult(new WorkflowApprovalSubmissionResult(false, null, "无待审批项"));
    }
}
