using AgentContracts;
using AgentGatewayHost;
using DomainSchemas;
using PlatformCore;
using QualityGate;
using System.Diagnostics;

namespace PlatformSelfCheck.Tests;

public sealed class WorkflowBackedInternalOrchestrationTests
{
    [Fact]
    public async Task InternalAgentWorkflowStepCallsMechanicalDesignerAndEvaluatesQualityGate()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var router = new InternalAgentRouter(platform.AgentRegistry, platform.AuditLog);
        var step = new InternalAgentWorkflowStep(
            "mechanical-designer",
            CreateAgentContext(),
            router,
            platform.AgentRegistry,
            platform.AuditLog);

        var result = await step.ExecuteAsync(new WorkflowContext("internal-step-test", new Dictionary<string, object?>()));

        Assert.Equal("internal-agent:mechanical-designer", result.StepId);
        Assert.NotNull(result.AgentOutput);
        Assert.NotNull(result.ReviewReport);
        Assert.NotNull(result.GateDecision);
        Assert.Equal(GateDecisionResult.Passed, result.GateDecision!.Result);
        Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "quality_gate_after_internal_step");
    }

    [Fact]
    public async Task ChiefEngineerInternalOrchestrationUsesWorkflowEngine()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer")!;

        var output = await chiefEngineer.ExecuteAsync(CreateAgentContext());
        var report = output.InternalCollaborationReport!;

        Assert.NotNull(report);
        Assert.NotNull(report.WorkflowId);
        Assert.Equal("Passed", report.WorkflowStatus);
        var stepResults = Assert.IsAssignableFrom<IReadOnlyList<InternalWorkflowStepSummary>>(report.StepResults);
        Assert.Equal(4, stepResults.Count);
        Assert.All(stepResults, step => Assert.NotNull(step.GateDecision));
        Assert.NotNull(report.FinalGateDecision);
        Assert.Contains(output.Logs, log => log.Contains("WorkflowEngine", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "workflow_started");
        Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "quality_gate_after_internal_step");
    }

    [Fact]
    public async Task InternalAgentWorkflowRejectedThenPassedRetriesThroughIRetryPolicy()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var router = new InternalAgentRouter(platform.AgentRegistry, platform.AuditLog);
        var step = new InternalAgentWorkflowStep(
            "mechanical-designer",
            CreateAgentContext("mechanical_retry_then_passed"),
            router,
            platform.AgentRegistry,
            platform.AuditLog);
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(maxRetries: 2), platform.AuditLog);

        var result = await engine.ExecuteAsync(
            new[] { step.ToWorkflowStep() },
            new WorkflowContext("internal-retry-test", new Dictionary<string, object?>()));

        Assert.Equal(WorkflowStatus.Passed, result.Status);
        Assert.Contains(result.Steps, stepResult => stepResult.Status == WorkflowStepStatus.Retrying);
        Assert.Equal(2, platform.AuditLog.GetEntries().Count(entry =>
            entry.Action == "internal_agent_invoked" && entry.Actor == "mechanical-designer"));
    }

    [Fact]
    public async Task InternalAgentWorkflowStopsAndCreatesFailureReportAfterMaxRetries()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var router = new InternalAgentRouter(platform.AgentRegistry, platform.AuditLog);
        var step = new InternalAgentWorkflowStep(
            "cad-modeler",
            CreateAgentContext("cad_max_retries_exceeded"),
            router,
            platform.AgentRegistry,
            platform.AuditLog);
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(maxRetries: 1), platform.AuditLog);

        var result = await engine.ExecuteAsync(
            new[] { step.ToWorkflowStep() },
            new WorkflowContext("internal-max-retry-test", new Dictionary<string, object?>()));

        Assert.Equal(WorkflowStatus.Rejected, result.Status);
        Assert.NotNull(result.Steps.Last().RejectReport);
        Assert.NotNull(result.FailureReport);
        Assert.Equal("internal-agent:cad-modeler", result.FailureReport!.FailedStepId);
    }

    [Fact]
    public async Task InternalAgentWorkflowFailedStepStopsWorkflow()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var router = new InternalAgentRouter(platform.AgentRegistry, platform.AuditLog);
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(), platform.AuditLog);
        var steps = new[]
        {
            new InternalAgentWorkflowStep("drawing-engineer", CreateAgentContext("drawing_engineer_failed"), router, platform.AgentRegistry, platform.AuditLog).ToWorkflowStep(),
            new InternalAgentWorkflowStep("drawing-reviewer", CreateAgentContext(), router, platform.AgentRegistry, platform.AuditLog).ToWorkflowStep()
        };

        var result = await engine.ExecuteAsync(steps, new WorkflowContext("internal-failed-test", new Dictionary<string, object?>()));

        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.NotNull(result.FailureReport);
        Assert.DoesNotContain(result.Steps, step => step.StepId == "internal-agent:drawing-reviewer");
    }

    [Fact]
    public async Task InternalAgentWorkflowHumanApprovalStepStopsWorkflow()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var router = new InternalAgentRouter(platform.AgentRegistry, platform.AuditLog);
        var engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(), platform.AuditLog);
        var steps = new[]
        {
            new InternalAgentWorkflowStep("drawing-reviewer", CreateAgentContext("drawing_reviewer_needs_human_approval"), router, platform.AgentRegistry, platform.AuditLog).ToWorkflowStep(),
            new InternalAgentWorkflowStep("mechanical-designer", CreateAgentContext(), router, platform.AgentRegistry, platform.AuditLog).ToWorkflowStep()
        };

        var result = await engine.ExecuteAsync(steps, new WorkflowContext("internal-human-test", new Dictionary<string, object?>()));

        Assert.Equal(WorkflowStatus.WaitingForHumanApproval, result.Status);
        Assert.NotNull(result.HumanApprovalRequest);
        Assert.DoesNotContain(result.Steps, step => step.StepId == "internal-agent:mechanical-designer");
    }

    [Fact]
    public void ExponentialBackoffRetryPolicyCalculatesIncreasingDelay()
    {
        IRetryPolicy policy = new ExponentialBackoffRetryPolicy(baseDelayMs: 100, maxDelayMs: 1000, multiplier: 2);

        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.GetDelay(0));
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(400), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), policy.GetDelay(10));
        Assert.True(policy.ShouldRetry(
            new GateDecision("gate-retry", GateDecisionResult.Rejected, "retryable"),
            0,
            new[] { Issue.FromText("retryable issue") }));
    }

    [Fact]
    public async Task SequentialWorkflowEngineAwaitsRetryDelayBeforeNextAttempt()
    {
        var delay = TimeSpan.FromMilliseconds(75);
        var engine = new SequentialWorkflowEngine(new FixedDelayRetryPolicy(delay), new InMemoryAuditLog());
        var attempts = 0;
        var step = new WorkflowStep(
            "delayed-retry-step",
            _ =>
            {
                attempts++;
                var decision = attempts == 1
                    ? new GateDecision("gate-retry", GateDecisionResult.Rejected, "retryable transient issue")
                    : new GateDecision("gate-pass", GateDecisionResult.Passed, "ok");

                return Task.FromResult(new WorkflowStepResult(
                    "delayed-retry-step",
                    "delayed-retry-step",
                    attempts == 1 ? WorkflowStepStatus.Rejected : WorkflowStepStatus.Passed,
                    attempts == 1 ? "transient rejection" : "passed",
                    GateDecision: decision,
                    Issues: attempts == 1 ? new[] { "retryable transient issue" } : Array.Empty<string>()));
            });

        var stopwatch = Stopwatch.StartNew();
        var result = await engine.ExecuteAsync(new[] { step }, new WorkflowContext("retry-delay-test", new Dictionary<string, object?>()));
        stopwatch.Stop();

        Assert.Equal(WorkflowStatus.Passed, result.Status);
        Assert.Equal(2, attempts);
        Assert.Contains(result.Steps, stepResult => stepResult.Status == WorkflowStepStatus.Retrying);
        Assert.True(stopwatch.Elapsed >= delay, $"Expected retry delay of at least {delay.TotalMilliseconds}ms, got {stopwatch.Elapsed.TotalMilliseconds}ms.");
    }

    [Fact]
    public async Task CodeEngineeringAgentsAreInternalAndBlockedByGateway()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var directory = new AgentDirectoryService(platform.AgentRegistry);
        var dispatcher = new AgentMessageDispatcher(platform);

        Assert.Equal(AgentVisibility.Internal, platform.AgentRegistry.GetById("code-engineer")!.Visibility);
        Assert.Equal(AgentVisibility.Internal, platform.AgentRegistry.GetById("code-reviewer")!.Visibility);
        Assert.Single(directory.GetVisibleAgents());
        Assert.Equal("chief-engineer", directory.GetVisibleAgents()[0].Id);
        Assert.Null(await dispatcher.DispatchAsync("code-engineer", CreateGatewayRequest()));
        Assert.Null(await dispatcher.DispatchAsync("code-reviewer", CreateGatewayRequest()));
    }

    [Fact]
    public async Task SelfCheckReportContainsWorkflowBackedInternalOrchestrationFields()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_v06", Guid.NewGuid().ToString("N"));

        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, FindProjectRoot());

            Assert.True(report.ChiefEngineerInternalOrchestrationUsesWorkflowEngine);
            Assert.True(report.InternalAgentWorkflowStepsCreated);
            Assert.True(report.QualityGateAfterEachInternalStep);
            Assert.Equal("Passed", report.InternalWorkflowPassedScenario);
            Assert.Equal("Passed", report.InternalWorkflowRetryThenPassedScenario);
            Assert.Equal("Passed", report.InternalWorkflowMaxRetriesExceededScenario);
            Assert.Equal("Passed", report.InternalWorkflowFailedScenario);
            Assert.Equal("Passed", report.InternalWorkflowHumanApprovalScenario);
            Assert.True(report.RetryPolicyInterfaceEnabled);
            Assert.True(report.ExponentialBackoffPolicyAvailable);
            Assert.True(report.CodeEngineerAgentRegistered);
            Assert.True(report.CodeReviewerAgentRegistered);
            Assert.True(report.CodeAgentsAreInternal);
            Assert.True(report.GatewayBlocksCodeAgents);
            Assert.Equal("Passed", report.FinalStatus);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static AgentContext CreateAgentContext(string? testScenario = null)
    {
        var context = new Dictionary<string, string> { ["project_id"] = "demo-project" };
        if (testScenario is not null)
        {
            context["test_scenario"] = testScenario;
        }

        var input = new AgentInput(
            "test",
            "test-channel",
            $"conversation-{Guid.NewGuid():N}",
            "user",
            "Plan a simple mechanical design workflow.",
            Array.Empty<string>(),
            context);

        return new AgentContext(
            $"task-{Guid.NewGuid():N}",
            input,
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);
    }

    private static GatewayMessageRequest CreateGatewayRequest() =>
        new(
            "openclaw",
            "demo-channel",
            "project-001",
            "user",
            "Plan a simple mechanical design workflow.",
            Array.Empty<string>(),
            new Dictionary<string, string> { ["project_id"] = "demo-project" });

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate project root.");
    }

    private sealed class FixedDelayRetryPolicy : DefaultRetryPolicy
    {
        private readonly TimeSpan _delay;

        public FixedDelayRetryPolicy(TimeSpan delay)
            : base(maxRetries: 1)
        {
            _delay = delay;
        }

        public override TimeSpan GetDelay(int retryCount) => _delay;
    }
}
