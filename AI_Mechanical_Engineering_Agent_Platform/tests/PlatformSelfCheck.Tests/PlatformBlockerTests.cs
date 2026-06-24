using AgentContracts;
using AgentGatewayHost;
using DomainSchemas;
using PlatformCore;
using Storage;

namespace PlatformSelfCheck.Tests;

public sealed class PlatformBlockerTests
{
    private static readonly string[] ExpectedModules =
    [
        "RequirementUnderstanding",
        "MechanicalDesign",
        "CADModeling",
        "DrawingGeneration",
        "DrawingReview",
        "CodeEngineering",
        "CodeReview",
        "ErrorDiagnosis"
    ];

    private static readonly string[] StandardModuleEntries =
    [
        "README.md",
        "module.yaml",
        "agents",
        "skills",
        "workers",
        "validators",
        "reviewers",
        "schemas",
        "tests"
    ];

    [Fact]
    public void EveryModuleContainsStandardSubdirectoriesAndGitkeepFiles()
    {
        var root = FindProjectRoot();

        foreach (var module in ExpectedModules)
        {
            var moduleRoot = Path.Combine(root, "src", "Modules", module);
            Assert.True(Directory.Exists(moduleRoot), $"{module} directory is missing.");

            foreach (var entry in StandardModuleEntries)
            {
                var path = Path.Combine(moduleRoot, entry);
                if (entry.Contains('.'))
                {
                    Assert.True(File.Exists(path), $"{module}/{entry} is missing.");
                    continue;
                }

                Assert.True(Directory.Exists(path), $"{module}/{entry}/ is missing.");
                Assert.True(File.Exists(Path.Combine(path, ".gitkeep")), $"{module}/{entry}/.gitkeep is missing.");
            }
        }
    }

    [Fact]
    public void ModuleManifestLoaderLoadsYamlManifests()
    {
        var loader = new ModuleManifestLoader();

        var result = loader.LoadFromModulesDirectory(Path.Combine(FindProjectRoot(), "src", "Modules"));

        Assert.Empty(result.Errors);
        Assert.Equal(8, result.Manifests.Count);
        Assert.Contains(result.Manifests, manifest => manifest.Name == "CADModeling");
        Assert.Contains(result.Manifests.Single(manifest => manifest.Name == "CADModeling").Workers, worker => worker == "FakeSolidWorksWorker");
    }

    [Fact]
    public void BootstrapperRegistersModulesFromYamlWhenAvailable()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());

        Assert.Equal(8, platform.ModuleRegistry.GetAll().Count);
        Assert.All(platform.ModuleRegistry.GetSources(), source => Assert.Equal("yaml", source.Source));
    }

    [Fact]
    public async Task GatewayRejectsInternalAgentMessages()
    {
        var dispatcher = new AgentMessageDispatcher(PlatformBootstrapper.CreateDefault(FindProjectRoot()));

        var response = await dispatcher.DispatchAsync("mechanical-designer", CreateGatewayRequest());

        Assert.Null(response);
    }

    [Fact]
    public async Task GatewayChiefEngineerDispatchRunsThroughQualityGate()
    {
        var dispatcher = new AgentMessageDispatcher(PlatformBootstrapper.CreateDefault(FindProjectRoot()));

        var response = await dispatcher.DispatchAsync("chief-engineer", CreateGatewayRequest());

        Assert.NotNull(response);
        Assert.Equal(GateDecisionResult.Passed, response!.GateDecision.Result);
        Assert.Equal("completed", response.Status);
        Assert.Null(response.RejectReport);
    }

    [Fact]
    public async Task GatewayRejectedDecisionIncludesRejectReport()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        platform.AgentRegistry.Register(new GatewayTestAgent(
            "public-problem-agent",
            AgentVisibility.Public,
            new AgentOutput(
                AgentOutputStatus.Completed,
                "Output has quality issues.",
                Array.Empty<ArtifactInfo>(),
                ["missing structured decomposition"],
                Array.Empty<string>(),
                null)));

        var dispatcher = new AgentMessageDispatcher(platform);

        var response = await dispatcher.DispatchAsync("public-problem-agent", CreateGatewayRequest());

        Assert.NotNull(response);
        Assert.Equal("rejected", response!.Status);
        Assert.Equal(GateDecisionResult.Rejected, response.GateDecision.Result);
        Assert.NotNull(response.RejectReport);
        Assert.Contains("missing structured decomposition", response.RejectReport!.Reasons);
    }

    [Fact]
    public async Task SelfCheckReportContainsBlockerFields()
    {
        var platform = PlatformBootstrapper.CreateDefault(FindProjectRoot());
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_blockers", Guid.NewGuid().ToString("N"));

        var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, FindProjectRoot());

        Assert.True(report.SolutionExists);
        Assert.Equal(8, report.ModuleStructureChecks.Count);
        Assert.Equal("yaml", report.ModuleManifestSource);
        Assert.Equal(8, report.YamlLoadedModules.Count);
        Assert.Empty(report.FallbackModules);
        Assert.True(report.GatewayQualityGateEnabled);
        Assert.True(report.StorageContractsRegistered);
        Assert.True(report.RejectReportBuilderCheck);
        Assert.Equal("Passed", report.FinalStatus);
    }

    [Fact]
    public void StorageContractsAreLoadableTypes()
    {
        Assert.NotNull(typeof(ITaskRepository));
        Assert.NotNull(typeof(IAuditLogRepository));
        Assert.NotNull(typeof(IEventStore));
        Assert.NotNull(typeof(IArtifactRepository));
        Assert.NotNull(typeof(IReportRepository));
    }

    private static GatewayMessageRequest CreateGatewayRequest() =>
        new(
            "openclaw",
            "demo-channel",
            "project-001",
            "user",
            "请帮我拆解一个机械设计任务",
            Array.Empty<string>(),
            new Dictionary<string, string> { ["project_id"] = "demo-project" });

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.slnx")) ||
                File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate project root.");
    }

    private sealed class GatewayTestAgent : IAgent
    {
        private readonly AgentOutput _output;

        public GatewayTestAgent(string id, AgentVisibility visibility, AgentOutput output)
        {
            Id = id;
            Visibility = visibility;
            _output = output;
        }

        public string Id { get; }

        public string Name => Id;

        public AgentRole Role => new(Id, Id, "Test agent");

        public string Description => "Test agent";

        public AgentVisibility Visibility { get; }

        public Task<AgentOutput> ExecuteAsync(AgentContext context) => Task.FromResult(_output);
    }
}
