using AgentContracts;
using AgentGatewayHost;
using DomainSchemas;
using PlatformCore;

namespace PlatformSelfCheck.Tests;

public sealed class InternalAgentRoutingTests
{
    private static readonly string[] ExpectedInternalRoute =
    [
        "mechanical-designer",
        "cad-modeler",
        "drawing-engineer",
        "drawing-reviewer"
    ];

    [Fact]
    public async Task InternalAgentRouterInvokesInternalAgentsSequentiallyAndAuditsCalls()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var router = new InternalAgentRouter(platform.AgentRegistry, platform.AuditLog);
        var context = CreateAgentContext();

        var outputs = await router.InvokeInternalAgentsSequentiallyAsync(ExpectedInternalRoute, context);

        Assert.Equal(ExpectedInternalRoute.Length, outputs.Count);
        Assert.All(outputs, output => Assert.Equal(AgentOutputStatus.Completed, output.Status));
        Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "internal_agent_invoked" && entry.Actor == "mechanical-designer");
        Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "internal_agent_completed" && entry.Actor == "drawing-reviewer");
    }

    [Fact]
    public async Task ChiefEngineerCreatesInternalCollaborationReportWithExpectedCalledAgents()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer")!;

        var output = await chiefEngineer.ExecuteAsync(CreateAgentContext());

        Assert.Equal(AgentOutputStatus.Completed, output.Status);
        Assert.NotNull(output.InternalCollaborationReport);
        Assert.Equal(ExpectedInternalRoute, output.InternalCollaborationReport!.CalledAgents.Select(agent => agent.AgentId));
        Assert.Equal("drawing-reviewer", output.InternalCollaborationReport.CalledAgents.Last().AgentId);
        Assert.Contains(output.Artifacts, artifact => artifact.Kind == "internal-collaboration-report");
    }

    [Fact]
    public async Task GatewayChiefEngineerResponseIncludesCollaborationReportAfterQualityGate()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var dispatcher = new AgentMessageDispatcher(platform);

        var response = await dispatcher.DispatchAsync("chief-engineer", CreateGatewayRequest());

        Assert.NotNull(response);
        Assert.Equal("completed", response!.Status);
        Assert.Equal(GateDecisionResult.Passed, response.GateDecision.Result);
        Assert.NotNull(response.CollaborationReport);
        Assert.Equal(ExpectedInternalRoute, response.CollaborationReport!.CalledAgents.Select(agent => agent.AgentId));
        Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "quality_gate_evaluated");
        Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "gateway_response_returned");
    }

    [Fact]
    public async Task GatewayStillBlocksDirectInternalAgentCalls()
    {
        var dispatcher = new AgentMessageDispatcher(PlatformBootstrapper.CreateDefault(FindProjectRoot()));

        Assert.Null(await dispatcher.DispatchAsync("mechanical-designer", CreateGatewayRequest()));
        Assert.Null(await dispatcher.DispatchAsync("cad-modeler", CreateGatewayRequest()));
    }

    [Fact]
    public async Task InternalAgentRouterConvertsInternalAgentExceptionToFailedOutput()
    {
        var registry = new AgentRegistry();
        var auditLog = new InMemoryAuditLog();
        registry.Register(new ThrowingInternalAgent());
        var router = new InternalAgentRouter(registry, auditLog);

        var output = await router.InvokeInternalAgentAsync("throwing-internal-agent", CreateAgentContext());

        Assert.Equal(AgentOutputStatus.Failed, output.Status);
        Assert.Equal("error-diagnosis", output.NextRecommendedAgentId);
        Assert.Contains(output.Issues, issue => issue.Contains("internal_agent_exception", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(output.Issues, issue => issue.Contains("InvalidOperationException", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(auditLog.GetEntries(), entry => entry.Action == "internal_agent_invoked" && entry.Actor == "throwing-internal-agent");
        Assert.Contains(auditLog.GetEntries(), entry => entry.Action == "internal_agent_failed" && entry.Actor == "throwing-internal-agent");
        Assert.DoesNotContain(auditLog.GetEntries(), entry => entry.Action == "internal_agent_completed" && entry.Actor == "throwing-internal-agent");
    }

    [Fact]
    public void QualityGateCanEvaluateCollaborationReportOutput()
    {
        var report = new InternalCollaborationReport(
            "conversation-001",
            "chief-engineer",
            new[]
            {
                new CalledAgentSummary("mechanical-designer", "Mechanical Designer", "Design", "Completed", "ok", "cad-modeler")
            },
            new[]
            {
                new AgentOutputSnapshot("mechanical-designer", "Completed", "ok", Array.Empty<ArtifactInfo>(), Array.Empty<string>(), null, null)
            },
            Array.Empty<string>(),
            Array.Empty<ArtifactInfo>(),
            "Collaboration completed.",
            "Proceed to QualityGate.");
        var output = new AgentOutput(
            AgentOutputStatus.Completed,
            "Chief engineer completed collaboration.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            report);

        var review = AgentOutputReviewMapper.ToReviewReport("test-quality-gate", output);

        Assert.True(review.IsPassed);
        Assert.Equal(1.0, review.Score);
    }

    [Fact]
    public async Task SelfCheckReportContainsInternalRoutingFields()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_routing", Guid.NewGuid().ToString("N"));

        var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, FindProjectRoot());

        Assert.True(report.InternalRoutingEnabled);
        Assert.Equal(ExpectedInternalRoute, report.InternalAgentsInvoked);
        Assert.True(report.CollaborationReportCreated);
        Assert.True(report.GatewayBlocksInternalAgents);
        Assert.True(report.QualityGateAfterCollaboration);
        Assert.True(report.AuditInternalAgentCalls);
        Assert.True(report.FeatureProductionEvidenceActive);
        Assert.Equal("Failed", report.FinalStatus);
    }

    private static AgentContext CreateAgentContext()
    {
        var input = new AgentInput(
            "test",
            "test-channel",
            "conversation-001",
            "user",
            "Plan a simple mechanical design workflow.",
            Array.Empty<string>(),
            new Dictionary<string, string> { ["project_id"] = "demo-project" });

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

    private sealed class ThrowingInternalAgent : IAgent
    {
        public string Id => "throwing-internal-agent";

        public string Name => "Throwing Internal Agent";

        public AgentRole Role { get; } = new("throwing", "Throwing", "Throws for router failure tests.");

        public string Description => "Throws for router failure tests.";

        public AgentVisibility Visibility => AgentVisibility.Internal;

        public Task<AgentOutput> ExecuteAsync(AgentContext context) =>
            throw new InvalidOperationException("test internal agent failure");
    }
}
