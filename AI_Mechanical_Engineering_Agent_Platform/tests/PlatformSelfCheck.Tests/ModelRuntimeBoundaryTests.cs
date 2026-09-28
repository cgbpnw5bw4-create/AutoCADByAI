using AgentContracts;
using AgentRuntime.Microsoft;
using DomainSchemas;
using ModelRuntime;
using PlatformCore;

namespace PlatformSelfCheck.Tests;

public sealed class ModelRuntimeBoundaryTests
{
    [Theory]
    [InlineData("chief-engineer")]
    [InlineData("mechanical-designer")]
    public void StandaloneLegacyMockAdapterCanStillPassThroughFactory(string agentId)
    {
        var factory = new AgentFactory();
        var standalone = factory.CreateMockAgent(agentId);
        var result = factory.CreateRuntimeAwareAgent(standalone, new AgentRegistry(),
            Configuration() with { EffectiveMode = AgentRuntimeMode.Mock });
        Assert.Same(standalone, result);
    }

    [Fact]
    public async Task ReconfigurationReplacesProviderAndMockRestoresOriginalAgent()
    {
        var platform = PlatformBootstrapper.CreateDefault();
        var original = platform.AgentRegistry.GetById("chief-engineer")!;
        var first = new StubProvider("第一提供商");
        var second = new StubProvider("第二提供商");
        RuntimePlatformFactory.ApplyRuntimeConfiguration(platform, Configuration(), modelProvider: first);
        await platform.AgentRegistry.GetById("chief-engineer")!.ExecuteAsync(Context());
        RuntimePlatformFactory.ApplyRuntimeConfiguration(platform, Configuration(), modelProvider: second);
        var replaced = await platform.AgentRegistry.GetById("chief-engineer")!.ExecuteAsync(Context());
        Assert.Contains("第二提供商", replaced.Message);
        Assert.DoesNotContain("第一提供商", replaced.Message);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);

        RuntimePlatformFactory.ApplyRuntimeConfiguration(platform, Configuration() with { EffectiveMode = AgentRuntimeMode.Mock });
        Assert.Same(original, platform.AgentRegistry.GetById("chief-engineer"));
        await original.ExecuteAsync(Context());
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task PendingApprovalRetainsItsRuntimeAndRejectsReconfiguration()
    {
        var platform = PlatformBootstrapper.CreateDefault(internalWorkflowRoute: InternalWorkflowRoute.EngineeringDefault);
        var provider = new StubProvider("工程建议");
        RuntimePlatformFactory.ApplyRuntimeConfiguration(platform, Configuration(), modelProvider: provider);
        var chief = platform.AgentRegistry.GetById("chief-engineer")!;
        var context = Context();
        context = context with { Input = context.Input with { Context = new Dictionary<string, string>
            { ["test_scenario"] = "drawing_reviewer_needs_human_approval" } } };
        var output = await chief.ExecuteAsync(context);
        var pending = output.InternalCollaborationReport!.HumanApprovalRequest!;
        Assert.NotNull(pending);
        Assert.Throws<InvalidOperationException>(() => RuntimePlatformFactory.ApplyRuntimeConfiguration(platform,
            Configuration() with { EffectiveMode = AgentRuntimeMode.Mock }));
        Assert.Same(chief, platform.AgentRegistry.GetById("chief-engineer"));
        var resumed = await ((IHumanApprovalAgent)chief).ResumeHumanApprovalAsync(context,
            new WorkflowApprovalSubmission(pending.WorkflowId, WorkflowApprovalDecision.Approve, "test",
                ApprovalRequestId: pending.ApprovalRequestId, StepId: pending.StepId));
        Assert.True(resumed.Accepted);
        Assert.Equal(AgentOutputStatus.Completed, resumed.Output!.Status);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesThroughCompatibilityInvoker()
    {
        using var source = new CancellationTokenSource();
        var provider = new CancellingProvider(source);
        var invoker = new MicrosoftRuntimeAgentInvoker(Configuration(), new ModelRuntime.ModelRuntime(provider), new InMemoryAuditLog());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.InvokeAsync(
            RuntimeAgentManifest.Create("chief-engineer"), Context(), source.Token));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ProviderCanBeReplacedInExistingChiefEngineerExecution()
    {
        foreach (var response in new[] { "提供商甲的工程建议", "提供商乙的工程建议" })
        {
            var provider = new StubProvider(response);
            var platform = PlatformBootstrapper.CreateDefault();
            var configuration = Configuration();
            RuntimePlatformFactory.ApplyRuntimeConfiguration(platform, configuration, modelProvider: provider);
            var output = await platform.AgentRegistry.GetById("chief-engineer")!.ExecuteAsync(Context());

            Assert.Contains(response, output.Message);
            Assert.Equal(1, provider.Calls);
            Assert.Equal(EngineeringModelPurpose.RequirementUnderstanding, provider.Request!.Purpose);
            Assert.Equal(AgentOutputStatus.Completed, output.Status);
            Assert.Equal("Passed", output.InternalCollaborationReport!.WorkflowStatus);
            Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action == "workflow_started");
            Assert.DoesNotContain(output.Artifacts, artifact => artifact.Path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task ProviderToolTextCannotBecomeWorkerInvocationOrEvidence()
    {
        var provider = new StubProvider("""{"status":"completed","message":"建议","tools":["worker:FakeSolidWorksWorker"],"next_recommended_agent_id":"SolidWorks.FeatureExtrusion3","api_evidence":"verified"}""");
        var invoker = new MicrosoftRuntimeAgentInvoker(Configuration(), new ModelRuntime.ModelRuntime(provider), new InMemoryAuditLog());
        var output = await invoker.InvokeAsync(RuntimeAgentManifest.Create("chief-engineer"), Context());

        Assert.Empty(output.Artifacts);
        Assert.Null(output.NextRecommendedAgentId);
        Assert.Contains(output.Issues, issue => issue.Contains("Worker", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ToolBridge).GetMethods(), method => method.Name == "InvokeWorkerAsync");
        Assert.DoesNotContain(typeof(ModelRuntime.ModelRuntime).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name!.Contains("Worker", StringComparison.Ordinal) || reference.Name.Contains("Microsoft.Agents", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task EmptyProviderResponseFailsClosed(string? text)
    {
        var runtime = new ModelRuntime.ModelRuntime(new StubProvider(text!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.GenerateAsync(Request()));
    }

    [Fact]
    public async Task CancellationAndInvalidPurposePreventProviderCalls()
    {
        var provider = new StubProvider("工程建议");
        var runtime = new ModelRuntime.ModelRuntime(provider);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.GenerateAsync(Request(), new CancellationToken(true)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runtime.GenerateAsync(Request() with { Purpose = (EngineeringModelPurpose)999 }));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ProviderReceivesCancellationAndFailureIsNotConvertedToSuccessfulText()
    {
        var provider = new StubProvider("建议") { Failure = new InvalidOperationException("provider_failed") };
        using var source = new CancellationTokenSource();
        var runtime = new ModelRuntime.ModelRuntime(provider);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.GenerateAsync(Request(), source.Token));
        Assert.Equal("provider_failed", exception.Message);
        Assert.Equal(source.Token, provider.Token);
    }

    private static ModelRequest Request() => new(EngineeringModelPurpose.DesignPlanning, "工程规划", "制定零件建模计划");
    private static RuntimeConfiguration Configuration() => new(AgentRuntimeMode.Microsoft, AgentRuntimeMode.Microsoft,
        "injected-provider", "configured-model", null, null, null, 60, false, null, false);
    private static AgentContext Context() => new($"runtime-boundary-{Guid.NewGuid():N}",
        new AgentInput("test", "test", "test", "test", "分析工程设计需求", [], new Dictionary<string, string>()),
        new Dictionary<string, object?>(), DateTimeOffset.UtcNow);

    private sealed class CancellingProvider(CancellationTokenSource source) : IModelProvider
    {
        public int Calls { get; private set; }
        public Task<string> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            source.Cancel();
            return Task.FromCanceled<string>(cancellationToken);
        }
    }

    private sealed class StubProvider(string response) : IModelProvider
    {
        public int Calls { get; private set; }
        public ModelRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Failure { get; init; }
        public Task<string> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            Token = cancellationToken;
            return Failure is null ? Task.FromResult(response) : Task.FromException<string>(Failure);
        }
    }
}
