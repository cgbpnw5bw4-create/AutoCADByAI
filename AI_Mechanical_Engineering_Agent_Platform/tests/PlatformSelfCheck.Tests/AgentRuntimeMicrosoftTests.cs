using AgentContracts;
using AgentRuntime.Microsoft;
using PlatformCore;
using System.Diagnostics;
using System.Net;

namespace PlatformSelfCheck.Tests;

public sealed class AgentRuntimeMicrosoftTests
{
    private static readonly string[] ExpectedAgentIds =
    [
        "chief-engineer",
        "mechanical-designer",
        "cad-modeler",
        "drawing-engineer",
        "drawing-reviewer",
        "code-engineer",
        "code-reviewer",
        "error-diagnosis"
    ];

    [Fact]
    public void MicrosoftAgentFrameworkPackageReferencesAreIsolatedToAgentRuntimeProject()
    {
        var root = FindProjectRoot();
        var projectFiles = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);
        var projectsWithMicrosoftAgentPackages = projectFiles
            .Where(project => File.ReadAllText(project).Contains("Microsoft.Agents", StringComparison.OrdinalIgnoreCase))
            .Select(project => Path.GetRelativePath(root, project).Replace('\\', '/'))
            .ToArray();

        Assert.All(projectsWithMicrosoftAgentPackages, project =>
            Assert.Equal("src/AgentRuntime.Microsoft/AgentRuntime.Microsoft.csproj", project));
    }

    [Fact]
    public void PlatformContractsDoNotExposeMicrosoftAgentFrameworkTypes()
    {
        var root = FindProjectRoot();
        var contractRoots = new[]
        {
            Path.Combine(root, "src", "AgentContracts"),
            Path.Combine(root, "src", "DomainSchemas"),
            Path.Combine(root, "src", "QualityGate"),
            Path.Combine(root, "src", "PlatformCore")
        };

        foreach (var file in contractRoots.SelectMany(path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Microsoft.Agents", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AgentFramework", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task MicrosoftAgentAdapterImplementsIAgentAndReturnsMockOutput()
    {
        var adapter = new MicrosoftAgentAdapter(
            RuntimeAgentManifest.Create("chief-engineer", AgentVisibility.Public),
            AgentRuntimeMode.Mock,
            new InMemoryAuditLog());

        Assert.IsAssignableFrom<IAgent>(adapter);

        var output = await adapter.ExecuteAsync(CreateAgentContext());

        Assert.Equal(AgentOutputStatus.Completed, output.Status);
        Assert.Contains("MockRuntime", output.Message);
    }

    [Fact]
    public void MockRuntimeCreatesAllPlatformAgentsWithCorrectVisibility()
    {
        var factory = new AgentFactory(new InMemoryAuditLog());

        var agents = ExpectedAgentIds.Select(factory.CreateMockAgent).ToArray();

        Assert.Equal(8, agents.Length);
        Assert.Equal("chief-engineer", Assert.Single(agents, agent => agent.Visibility == AgentVisibility.Public).Id);
        Assert.All(agents.Where(agent => agent.Id != "chief-engineer"), agent => Assert.Equal(AgentVisibility.Internal, agent.Visibility));
    }

    [Fact]
    public void BootstrapperCanRegisterAgentsFromRuntimeFactory()
    {
        var factory = new AgentFactory(new InMemoryAuditLog());

        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot(), factory.CreateMockAgent);

        Assert.Equal(8, platform.AgentRegistry.GetAll().Count);
        Assert.Equal("chief-engineer", Assert.Single(platform.AgentRegistry.GetPublicAgents()).Id);
        Assert.DoesNotContain(platform.AgentRegistry.GetPublicAgents(), agent => agent.Id == "mechanical-designer");
    }

    [Fact]
    public async Task MicrosoftWorkflowRuntimeRunsMockSequentialWorkflow()
    {
        var runtime = new MicrosoftWorkflowRuntime(AgentRuntimeMode.Mock);
        var context = new WorkflowContext("runtime-test", new Dictionary<string, object?>());

        var result = await runtime.ExecuteAsync(
            new[]
            {
                new WorkflowStep("step-1", _ => Task.FromResult(new WorkflowStepResult("step-1", "Completed", "ok"))),
                new WorkflowStep("step-2", _ => Task.FromResult(new WorkflowStepResult("step-2", "Completed", "ok")))
            },
            context);

        Assert.Equal("Passed", result.FinalStatus);
        Assert.Equal(2, result.Steps.Count);
    }

    [Fact]
    public async Task OpenAICompatibleModelClientRespectsConfiguredTimeout()
    {
        var handler = new DelayingHttpMessageHandler();
        var client = new OpenAICompatibleModelClient(new HttpClient(handler));
        var configuration = CreateMicrosoftRuntimeConfiguration(timeoutSeconds: 1);

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GenerateTextAsync("system", "user", configuration));
        stopwatch.Stop();

        Assert.True(handler.CancellationObserved);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Configured timeout should cancel quickly; elapsed {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task MicrosoftRuntimeAgentInvokerConvertsIOExceptionToStructuredFailure()
    {
        var auditLog = new InMemoryAuditLog();
        var invoker = new MicrosoftRuntimeAgentInvoker(
            CreateMicrosoftRuntimeConfiguration(),
            new ThrowingRuntimeModelClient(new System.IO.IOException("network stream closed")),
            auditLog,
            systemPrompt: "system");

        var output = await invoker.InvokeAsync(
            RuntimeAgentManifest.Create("chief-engineer", AgentVisibility.Public),
            CreateAgentContext());

        Assert.Equal(AgentOutputStatus.Failed, output.Status);
        Assert.Contains(output.Issues, issue => issue.Contains("IOException", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(auditLog.GetEntries(), entry =>
            entry.Action == "real_runtime_failed" &&
            entry.Message.Contains("IOException", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToolBridgeMapsToolCallsWithoutCallingWorkers()
    {
        var bridge = new ToolBridge(new SkillRegistry(), new WorkerRegistry());

        var skillMapping = bridge.MapToolCallToSkill(new RuntimeToolCall("skill:requirement-to-cad-model-spec", new Dictionary<string, string>()));
        var workerMapping = bridge.MapToolCallToWorker(new RuntimeToolCall("worker:FakeSolidWorksWorker", new Dictionary<string, string>()));

        Assert.Equal("requirement-to-cad-model-spec", skillMapping.TargetName);
        Assert.Equal("FakeSolidWorksWorker", workerMapping.TargetName);
    }

    [Fact]
    public async Task SelfCheckReportContainsRuntimeFields()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_runtime", Guid.NewGuid().ToString("N"));

        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, FindProjectRoot());

            Assert.True(report.AgentRuntimeProjectExists);
            Assert.True(report.MicrosoftRuntimeDependencyIsolated);
            Assert.Equal("Mock", report.RuntimeMode);
            Assert.True(report.MockRuntimeAgentCreation);
            Assert.True(report.MicrosoftAgentAdapterCheck);
            Assert.True(report.MicrosoftWorkflowRuntimeCheck);
            Assert.True(report.RuntimeTypesDoNotLeakToContracts);
            Assert.True(report.GatewayVisibilityStillValid);
            Assert.True(report.QualityGateStillEnabled);
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

    private static AgentContext CreateAgentContext()
    {
        var input = new AgentInput(
            "test",
            "runtime-test",
            "runtime-conversation",
            "user",
            "Run runtime adapter test.",
            Array.Empty<string>(),
            new Dictionary<string, string>());

        return new AgentContext(
            $"task-{Guid.NewGuid():N}",
            input,
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);
    }

    private static RuntimeConfiguration CreateMicrosoftRuntimeConfiguration(int timeoutSeconds = 30) =>
        new(
            AgentRuntimeMode.Microsoft,
            AgentRuntimeMode.Microsoft,
            "openai-compatible",
            "demo-model",
            "test-key",
            "http://runtime.test",
            null,
            timeoutSeconds,
            false,
            null,
            false);

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

    private sealed class DelayingHttpMessageHandler : HttpMessageHandler
    {
        public bool CancellationObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"ok"}}]}""")
            };
        }
    }

    private sealed class ThrowingRuntimeModelClient : IRuntimeModelClient
    {
        private readonly Exception _exception;

        public ThrowingRuntimeModelClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<string> GenerateTextAsync(
            string systemPrompt,
            string userMessage,
            RuntimeConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromException<string>(_exception);
    }
}
