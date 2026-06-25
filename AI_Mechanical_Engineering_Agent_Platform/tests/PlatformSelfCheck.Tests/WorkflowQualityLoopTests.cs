using AgentContracts;
using AgentGatewayHost;
using DomainSchemas;
using PlatformCore;
using QualityGate;

namespace PlatformSelfCheck.Tests;

public sealed class WorkflowQualityLoopTests
{
    [Fact]
    public async Task WorkflowEnginePassedStepContinuesToNextStep()
    {
        var secondStepExecuted = false;
        var engine = new SequentialWorkflowEngine(new RetryPolicy());

        var result = await engine.ExecuteAsync(
            new[]
            {
                new WorkflowStep("step-1", _ => Task.FromResult(Result("step-1", GateDecisionResult.Passed))),
                new WorkflowStep("step-2", _ =>
                {
                    secondStepExecuted = true;
                    return Task.FromResult(Result("step-2", GateDecisionResult.Passed));
                })
            },
            CreateWorkflowContext());

        Assert.True(secondStepExecuted);
        Assert.Equal(WorkflowStatus.Passed, result.Status);
        Assert.Equal(2, result.Steps.Count);
        Assert.All(result.Steps, step => Assert.Equal(WorkflowStepStatus.Passed, step.Status));
    }

    [Fact]
    public async Task WorkflowEngineRejectedStepRetriesAccordingToRetryPolicy()
    {
        var attemptCount = 0;
        var engine = new SequentialWorkflowEngine(new RetryPolicy(maxRetries: 2));

        var result = await engine.ExecuteAsync(
            new[]
            {
                new WorkflowStep("retry-step", _ =>
                {
                    attemptCount++;
                    return Task.FromResult(attemptCount == 1
                        ? Result("retry-step", GateDecisionResult.Rejected, ["retryable geometry planning issue"])
                        : Result("retry-step", GateDecisionResult.Passed));
                }),
                new WorkflowStep("final-step", _ => Task.FromResult(Result("final-step", GateDecisionResult.Passed)))
            },
            CreateWorkflowContext());

        Assert.Equal(2, attemptCount);
        Assert.Equal(WorkflowStatus.Passed, result.Status);
        Assert.Contains(result.Steps, step => step.StepId == "retry-step" && step.Status == WorkflowStepStatus.Retrying);
        Assert.Contains(result.AuditLogs, entry => entry.Action == "workflow_step_retrying");
    }

    [Fact]
    public async Task WorkflowEngineStopsWhenRejectedStepExceedsMaxRetries()
    {
        var attemptCount = 0;
        var engine = new SequentialWorkflowEngine(new RetryPolicy(maxRetries: 1));

        var result = await engine.ExecuteAsync(
            new[]
            {
                new WorkflowStep("always-rejected", _ =>
                {
                    attemptCount++;
                    return Task.FromResult(Result("always-rejected", GateDecisionResult.Rejected, ["persistent quality issue"]));
                }),
                new WorkflowStep("should-not-run", _ => Task.FromResult(Result("should-not-run", GateDecisionResult.Passed)))
            },
            CreateWorkflowContext());

        Assert.Equal(2, attemptCount);
        Assert.Equal(WorkflowStatus.Rejected, result.Status);
        Assert.NotNull(result.Steps.Last().RejectReport);
        Assert.Equal(GateDecisionResult.Rejected, result.FinalGateDecision?.Result);
        Assert.DoesNotContain(result.Steps, step => step.StepId == "should-not-run");
    }

    [Fact]
    public async Task WorkflowEngineFailedStepGeneratesFailureReport()
    {
        var engine = new SequentialWorkflowEngine(new RetryPolicy());

        var result = await engine.ExecuteAsync(
            new[]
            {
                new WorkflowStep("failed-step", _ => Task.FromResult(Result("failed-step", GateDecisionResult.Failed, ["fatal issue"]))),
                new WorkflowStep("should-not-run", _ => Task.FromResult(Result("should-not-run", GateDecisionResult.Passed)))
            },
            CreateWorkflowContext());

        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.NotNull(result.FailureReport);
        Assert.Equal("failed-step", result.FailureReport!.FailedStepId);
        Assert.Contains("fatal issue", result.FailureReport.Issues);
        Assert.DoesNotContain(result.Steps, step => step.StepId == "should-not-run");
    }

    [Fact]
    public async Task WorkflowEngineHumanApprovalStepGeneratesHumanApprovalRequest()
    {
        var downstreamExecuted = false;
        var engine = new SequentialWorkflowEngine(new RetryPolicy());

        var result = await engine.ExecuteAsync(
            new[]
            {
                new WorkflowStep("approval-step", _ => Task.FromResult(Result("approval-step", GateDecisionResult.NeedsHumanApproval, ["manual approval required"]))),
                new WorkflowStep("should-not-run", _ =>
                {
                    downstreamExecuted = true;
                    return Task.FromResult(Result("should-not-run", GateDecisionResult.Passed));
                })
            },
            CreateWorkflowContext());

        Assert.False(downstreamExecuted);
        Assert.Equal(WorkflowStatus.WaitingForHumanApproval, result.Status);
        Assert.NotNull(result.HumanApprovalRequest);
        Assert.Equal("approval-step", result.HumanApprovalRequest!.StepId);
    }

    [Fact]
    public void PlatformRegistersModuleAgentsInsteadOfPlaceholderAgents()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var agents = platform.AgentRegistry.GetAll();

        Assert.DoesNotContain(agents, agent => agent.GetType().Name == nameof(PlaceholderAgent));
        Assert.Equal("ChiefEngineerAgent", platform.AgentRegistry.GetById("chief-engineer")!.GetType().Name);
        Assert.Equal("MechanicalDesignerAgent", platform.AgentRegistry.GetById("mechanical-designer")!.GetType().Name);
        Assert.Equal("CadModelerAgent", platform.AgentRegistry.GetById("cad-modeler")!.GetType().Name);
        Assert.Equal("DrawingEngineerAgent", platform.AgentRegistry.GetById("drawing-engineer")!.GetType().Name);
        Assert.Equal("DrawingReviewerAgent", platform.AgentRegistry.GetById("drawing-reviewer")!.GetType().Name);
        Assert.Equal("ErrorDiagnosisAgent", platform.AgentRegistry.GetById("error-diagnosis")!.GetType().Name);
        Assert.Equal(AgentVisibility.Public, platform.AgentRegistry.GetById("chief-engineer")!.Visibility);
        Assert.All(platform.AgentRegistry.GetInternalAgents(), agent => Assert.Equal(AgentVisibility.Internal, agent.Visibility));
    }

    [Fact]
    public async Task GatewayStillOnlyExposesChiefEngineerAfterWorkflowQualityLoopChanges()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var directory = new AgentDirectoryService(platform.AgentRegistry);
        var dispatcher = new AgentMessageDispatcher(platform);

        var visibleAgents = directory.GetVisibleAgents();
        var internalResponse = await dispatcher.DispatchAsync("mechanical-designer", CreateGatewayRequest());

        Assert.Single(visibleAgents);
        Assert.Equal("chief-engineer", visibleAgents[0].Id);
        Assert.Null(internalResponse);
    }

    [Fact]
    public async Task SelfCheckReportContainsWorkflowQualityLoopFields()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_v04", Guid.NewGuid().ToString("N"));

        var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, FindProjectRoot());

        Assert.True(report.WorkflowQualityLoopEnabled);
        Assert.Equal("Passed", report.WorkflowPassedScenario);
        Assert.Equal("Passed", report.WorkflowRejectedRetryPassedScenario);
        Assert.Equal("Passed", report.WorkflowRejectedMaxRetriesScenario);
        Assert.Equal("Passed", report.WorkflowHumanApprovalScenario);
        Assert.True(report.RetryPolicyEnabled);
        Assert.True(report.FailureReportGenerated);
        Assert.True(report.HumanApprovalRequestGenerated);
        Assert.True(report.ModuleAgentsRegistered);
        Assert.True(report.PlaceholderAgentIsFallbackOnly);
        Assert.Equal("Passed", report.FinalStatus);
    }

    private static WorkflowContext CreateWorkflowContext() =>
        new($"workflow-{Guid.NewGuid():N}", new Dictionary<string, object?>());

    private static WorkflowStepResult Result(
        string stepId,
        GateDecisionResult decisionResult,
        IReadOnlyList<string>? issues = null)
    {
        var decision = new GateDecision(
            $"gate-{stepId}-{Guid.NewGuid():N}",
            decisionResult,
            $"{decisionResult} decision for {stepId}.",
            decisionResult == GateDecisionResult.Passed ? "next" : null);

        var status = decisionResult switch
        {
            GateDecisionResult.Passed => WorkflowStepStatus.Passed,
            GateDecisionResult.Rejected => WorkflowStepStatus.Rejected,
            GateDecisionResult.Failed => WorkflowStepStatus.Failed,
            GateDecisionResult.NeedsHumanApproval => WorkflowStepStatus.WaitingForHumanApproval,
            _ => WorkflowStepStatus.Running
        };

        return new WorkflowStepResult(
            stepId,
            stepId,
            status,
            $"Step {stepId} returned {decisionResult}.",
            GateDecision: decision,
            Issues: issues ?? Array.Empty<string>(),
            Logs: [$"log:{stepId}"]);
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
}
