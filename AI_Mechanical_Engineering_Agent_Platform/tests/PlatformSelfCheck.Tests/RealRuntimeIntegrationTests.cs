using AgentContracts;
using AgentGatewayHost;
using AgentRuntime.Microsoft;
using DomainSchemas;
using PlatformCore;

namespace PlatformSelfCheck.Tests;

public sealed class RealRuntimeIntegrationTests
{
    [Fact]
    public void RuntimeConfigurationDefaultsToMockAndFallsBackWhenApiKeyIsMissing()
    {
        var defaultConfig = RuntimeConfiguration.FromEnvironment(new Dictionary<string, string?>());
        var missingKeyConfig = RuntimeConfiguration.FromEnvironment(new Dictionary<string, string?>
        {
            ["AI_AGENT_RUNTIME_MODE"] = "Microsoft",
            ["AI_PROVIDER"] = "openai-compatible",
            ["AI_MODEL"] = "demo-model"
        });

        Assert.Equal(AgentRuntimeMode.Mock, defaultConfig.EffectiveMode);
        Assert.False(defaultConfig.FallbackUsed);
        Assert.Equal(AgentRuntimeMode.Microsoft, missingKeyConfig.RequestedMode);
        Assert.Equal(AgentRuntimeMode.Mock, missingKeyConfig.EffectiveMode);
        Assert.True(missingKeyConfig.FallbackUsed);
        Assert.DoesNotContain("api", missingKeyConfig.FallbackReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MicrosoftAgentOutputMapperParsesJsonAndFallsBackForInvalidJson()
    {
        var mapper = new MicrosoftAgentOutputMapper(["mechanical-designer", "cad-modeler", "drawing-engineer", "drawing-reviewer"]);
        var valid = mapper.Map(
            """
            {
              "status": "completed",
              "message": "已完成任务理解。",
              "task_summary": "板件建模和工程图复审",
              "recommended_internal_agents": ["mechanical-designer", "cad-modeler"],
              "risks": [],
              "next_recommended_agent_id": "mechanical-designer"
            }
            """,
            RuntimeMetadata.MockMicrosoft("openai-compatible", "demo-model"));
        var invalid = mapper.Map(
            "not-json model text",
            RuntimeMetadata.MockMicrosoft("openai-compatible", "demo-model"));

        Assert.Equal(AgentOutputStatus.Completed, valid.Status);
        Assert.Equal("mechanical-designer", valid.NextRecommendedAgentId);
        Assert.Empty(valid.Issues);
        Assert.Equal(AgentOutputStatus.Completed, invalid.Status);
        Assert.Contains("not-json model text", invalid.Message);
        Assert.Contains(invalid.Logs, log => log.Contains("fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MicrosoftAgentOutputMapperBlocksPermissionEscalationAndWorkerCalls()
    {
        var mapper = new MicrosoftAgentOutputMapper(["mechanical-designer"]);
        var output = mapper.Map(
            """
            {
              "status": "completed",
              "message": "unsafe",
              "recommended_internal_agents": ["mechanical-designer", "worker:FakeSolidWorksWorker"],
              "next_recommended_agent_id": "worker:FakeSolidWorksWorker",
              "visibility": "Public",
              "expose_internal_agents": true
            }
            """,
            RuntimeMetadata.MockMicrosoft("openai-compatible", "demo-model"));

        Assert.Contains(output.Issues, issue => issue.Contains("permission", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(output.Issues, issue => issue.Contains("Worker", StringComparison.OrdinalIgnoreCase));
        Assert.Null(output.NextRecommendedAgentId);
    }

    [Fact]
    public async Task AgentFactoryUsesRealRuntimeOnlyForChiefEngineer()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var factory = new AgentFactory(platform.AuditLog);
        var config = RuntimeConfiguration.FromEnvironment(new Dictionary<string, string?>
        {
            ["AI_AGENT_RUNTIME_MODE"] = "Microsoft",
            ["AI_PROVIDER"] = "openai-compatible",
            ["AI_MODEL"] = "demo-model",
            ["AI_API_KEY"] = "test-key"
        });
        var chief = platform.AgentRegistry.GetById("chief-engineer")!;
        var mechanical = platform.AgentRegistry.GetById("mechanical-designer")!;

        var runtimeChief = factory.CreateRuntimeAwareAgent(
            chief,
            platform.AgentRegistry,
            config,
            new FakeRuntimeModelClient());
        var runtimeMechanical = factory.CreateRuntimeAwareAgent(
            mechanical,
            platform.AgentRegistry,
            config,
            new FakeRuntimeModelClient());

        Assert.IsType<MicrosoftAgentAdapter>(runtimeChief);
        Assert.Same(mechanical, runtimeMechanical);
        var output = await runtimeChief.ExecuteAsync(CreateAgentContext());
        Assert.True(output.RuntimeMetadata?.ChiefEngineerRuntimeUsed);
        Assert.NotNull(output.InternalCollaborationReport);
        Assert.Equal("Passed", output.InternalCollaborationReport!.WorkflowStatus);
    }

    [Fact]
    public async Task GatewayResponseContainsRuntimeMetadataAndStillRunsQualityGate()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var factory = new AgentFactory(platform.AuditLog);
        var config = RuntimeConfiguration.FromEnvironment(new Dictionary<string, string?>
        {
            ["AI_AGENT_RUNTIME_MODE"] = "Microsoft",
            ["AI_PROVIDER"] = "openai-compatible",
            ["AI_MODEL"] = "demo-model",
            ["AI_API_KEY"] = "test-key"
        });
        var chief = platform.AgentRegistry.GetById("chief-engineer")!;
        platform.AgentRegistry.Register(factory.CreateRuntimeAwareAgent(
            chief,
            platform.AgentRegistry,
            config,
            new FakeRuntimeModelClient()));
        var dispatcher = new AgentMessageDispatcher(platform);

        var response = await dispatcher.DispatchAsync("chief-engineer", CreateGatewayRequest());

        Assert.NotNull(response);
        Assert.Equal("Microsoft", response!.RuntimeMode);
        Assert.Equal("openai-compatible", response.RuntimeProvider);
        Assert.Equal("demo-model", response.RuntimeModel);
        Assert.False(response.RuntimeFallbackUsed);
        Assert.True(response.ChiefEngineerRuntimeUsed);
        Assert.NotNull(response.GateDecision);
        Assert.Null(await dispatcher.DispatchAsync("mechanical-designer", CreateGatewayRequest()));
    }

    [Fact]
    public async Task SelfCheckReportContainsRealRuntimeFields()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_v07", Guid.NewGuid().ToString("N"));

        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, FindProjectRoot());

            Assert.True(report.RealRuntimeInvokerImplemented);
            Assert.True(report.RuntimeConfigEnvSupported);
            Assert.True(report.RuntimeModeDefaultIsMock);
            Assert.True(report.RuntimeFallbackWhenMissingKey);
            Assert.True(report.ChiefEngineerRealRuntimeOnly);
            Assert.True(report.InternalAgentsRemainMock);
            Assert.True(report.MicrosoftAgentOutputMapperEnabled);
            Assert.True(report.InvalidModelOutputFallbackEnabled);
            Assert.True(report.ModelCannotEscalatePermissions);
            Assert.True(report.ModelCannotCallWorkerDirectly);
            Assert.True(report.ChiefEngineerRuntimeThenWorkflowEngine);
            Assert.True(report.QualityGateAfterRealRuntime);
            Assert.True(report.GatewayResponseContainsRuntimeMetadata);
            Assert.True(report.FeatureProductionEvidenceActive);
            Assert.Equal("Failed", report.FinalStatus);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static AgentContext CreateAgentContext()
    {
        var input = new AgentInput(
            "test",
            "runtime-test",
            $"conversation-{Guid.NewGuid():N}",
            "user",
            "请拆解一个机械设计自动化任务。",
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
            "请拆解一个机械设计自动化任务。",
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

    private sealed class FakeRuntimeModelClient : IRuntimeModelClient
    {
        public Task<string> GenerateTextAsync(
            string systemPrompt,
            string userMessage,
            RuntimeConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                """
                {
                  "status": "completed",
                  "message": "已完成任务理解和内部协作建议。",
                  "task_summary": "机械设计自动化任务",
                  "recommended_internal_agents": ["mechanical-designer", "cad-modeler", "drawing-engineer", "drawing-reviewer"],
                  "risks": [],
                  "next_recommended_agent_id": "mechanical-designer"
                }
                """);
    }
}
