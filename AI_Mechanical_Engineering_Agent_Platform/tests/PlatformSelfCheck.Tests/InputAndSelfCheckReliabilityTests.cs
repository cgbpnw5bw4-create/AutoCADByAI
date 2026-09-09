using System.Text.Json;
using AgentContracts;
using AgentGatewayHost;
using DomainSchemas;
using PlatformCore;
using SolidWorksWorker;
using WorkerContracts;

namespace PlatformSelfCheck.Tests;

public sealed class InputAndSelfCheckReliabilityTests
{
    [Fact]
    public async Task StructuredInputSelfCheckCoversRejectionAndModelPreservation() =>
        Assert.True(await StructuredCadInputSelfCheck.RunAsync());

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"features\":42}")]
    [InlineData("{\"features\":[null]}")]
    [InlineData("{\"sketches\":[null]}")]
    [InlineData("{\"sketches\":[{\"entities\":[null]}]}")]
    [InlineData("{\"sketches\":[{\"constraints\":[null]}]}")]
    [InlineData("{\"features\":[{\"parameters\":{\"diameter_mm\":null}}]}")]
    [InlineData("{\"features\":[{\"parameters\":{\"x\":\"1\",\"X\":\"2\"}}]}")]
    public async Task ExplicitInvalidModelNeverFallsBackOrInvokesWorker(string json)
    {
        var root = PlatformPathResolver.FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(root);
        var worker = new RejectAnyWorker();
        platform.WorkerRegistry.Register(worker);
        foreach (var hint in new[] { "none", "solidworks_main_workflow", "part_type", "operation" })
        {
            var values = new Dictionary<string, string> { ["cad_model_spec_json"] = json, ["project_root"] = root };
            if (hint == "solidworks_main_workflow") values[hint] = "true";
            if (hint == "part_type") values[hint] = "plate_basic_4holes";
            if (hint == "operation") values[hint] = SolidWorksE2eCliContract.CompleteDrawingPackageOperation;
            var context = Context(values);
            var router = new SolidWorksWorkflowRouter();
            Assert.True(router.ShouldRun(context));
            Assert.Throws<ArgumentException>(() => router.TryBuildRequest(context));
            var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer",
                new("test", "test", Guid.NewGuid().ToString("N"), "tester", "结构化建模请求", [], values));
            Assert.NotNull(response);
            Assert.Equal("failed", response.Status);
            Assert.Contains(response.Issues, issue => issue.StartsWith("invalid_cad_model_spec:", StringComparison.Ordinal));
            Assert.Empty(response.Artifacts);
        }
        Assert.Equal(0, worker.InvocationCount);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData(" ")]
    [InlineData("[]")]
    public void InvalidParameterUpdateCannotBeSilentlyDiscarded(string json)
    {
        var values = new Dictionary<string, string>
        {
            ["parameter_update_json"] = json,
            ["part_type"] = "plate_basic_4holes",
            ["operation"] = SolidWorksE2eCliContract.ModelUpdateReleasePackageOperation
        };
        var context = Context(values);
        Assert.Contains(SolidWorksWorkflowRouter.ValidateStructuredInput(context), issue =>
            issue.StartsWith("invalid_parameter_update:", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new SolidWorksWorkflowRouter().TryBuildRequest(context));
    }

    [Fact]
    public async Task UpdateRequiresExplicitModelAndUpdateOperation()
    {
        var platform = PlatformBootstrapper.CreateDefault();
        var worker = new RejectAnyWorker();
        platform.WorkerRegistry.Register(worker);
        foreach (var hint in new[] { "none", "part_type", "operation", "wrong_operation" })
        {
            var values = new Dictionary<string, string> { ["parameter_update_json"] = "{\"new_parameters\":{\"length_mm\":\"200\"}}" };
            if (hint is "part_type" or "wrong_operation") values["part_type"] = "plate_basic_4holes";
            if (hint == "operation") values["operation"] = SolidWorksE2eCliContract.ModelUpdateReleasePackageOperation;
            if (hint == "wrong_operation") values["operation"] = SolidWorksE2eCliContract.CompleteDrawingPackageOperation;
            Assert.Throws<ArgumentException>(() => new SolidWorksWorkflowRouter().TryBuildRequest(Context(values)));
            var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer",
                new("test", "test", Guid.NewGuid().ToString("N"), "tester", "更新参数", [], values));
            Assert.NotNull(response);
            Assert.Equal("failed", response.Status);
            Assert.Contains(response.Issues, issue => issue.StartsWith("invalid_parameter_update:", StringComparison.Ordinal));
        }
        Assert.Equal(0, worker.InvocationCount);
    }

    [Fact]
    public void ValidStructuredModelWinsOverLegacyHint()
    {
        var original = PartFamilyRegressionModels.CreateDefault().First().Spec;
        var context = Context(new Dictionary<string, string>
        {
            ["cad_model_spec_json"] = JsonSerializer.Serialize(original),
            ["solidworks_main_workflow"] = "true", ["dry_run"] = "true",
            ["part_type"] = "unknown_hint"
        });
        var request = new SolidWorksWorkflowRouter().TryBuildRequest(context);
        Assert.NotNull(request);
        Assert.Equal(original.ModelId, request.ModelSpec!.ModelId);
        Assert.Equal(original.Features.Count, request.ModelSpec.Features.Count);
        Assert.True(request.DryRun);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"cad_model_spec\":null}")]
    [InlineData("{\"cad_model_spec\":{}}")]
    [InlineData("{\"cad_model_spec\":{\"features\":[null]}}")]
    [InlineData("{\"cad_model_spec\":{\"sketches\":[{\"entities\":[null]}]}}")]
    [InlineData("{\"cad_model_spec\":{\"sketches\":[{\"constraints\":[null]}]}}")]
    [InlineData("{\"cad_model_spec\":{\"features\":[{\"parameters\":{\"diameter_mm\":null}}]}}")]
    [InlineData("{\"cad_model_spec\":{\"features\":[{\"parameters\":{\"x\":\"1\",\"X\":\"2\"}}]}}")]
    public async Task BrokenFixtureIsUnobservedWithActionableIssueAndOtherTypesContinue(string? content)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "examples", "hole_tapped_plate.json");
        if (content is null) File.Delete(path);
        else await File.WriteAllTextAsync(path, content);
        var result = await V21BHoleSelfCheck.RunAsync(fixture.Root, fixture.Platform, CancellationToken.None, fixture.Output);
        Assert.False(result.GroupPassed);
        Assert.False(result.Value("hole_self_check_inputs_readable"));
        Assert.Null(result.Value("tapped_hole_supported"));
        Assert.Null(result.Value("unverified_hole_blocks_real_execution"));
        Assert.True(result.Value("simple_hole_supported"));
        Assert.True(result.Value("counterbore_hole_supported"));
        Assert.Contains(result.Issues, issue => issue.Contains(path, StringComparison.Ordinal) && issue.Contains("hole_self_check_input_invalid"));
        var gate = SelfCheckCapabilityRegressionGate.Evaluate(fixture.Root, result.Checks);
        Assert.False(gate.Passed);
        Assert.DoesNotContain("unverified_hole_blocks_real_execution", gate.RegressedFields);
        Assert.Contains(gate.ConfigurationErrors, issue => issue.Contains("unverified_hole_blocks_real_execution", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "output")));
    }

    [Fact]
    public async Task InvalidFixtureParametersDoNotDereferenceMissingDefinition()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "examples", "hole_tapped_plate.json");
        var text = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(text);
        var spec = doc.RootElement.GetProperty("cad_model_spec").Deserialize<CADModelSpec>()!;
        var hole = spec.Features.Single(HoleGeometryValidation.IsExplicitHole);
        spec = spec with { Features = spec.Features.Select(f => f == hole
            ? f with { Parameters = new Dictionary<string, string>(f.Parameters) { ["diameter_mm"] = "0" } } : f).ToArray() };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { cad_model_spec = spec }));
        var result = await V21BHoleSelfCheck.RunAsync(fixture.Root, fixture.Platform, CancellationToken.None, fixture.Output);
        Assert.False(result.GroupPassed);
        Assert.False(result.Value("tapped_hole_supported"));
        Assert.Contains(result.Issues, issue => issue.Contains("hole_self_check_fixture_rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteHoleCheckUsesOnlySelectedOutputAndDoesNotClaimXunitRan()
    {
        using var fixture = new Fixture();
        var result = await V21BHoleSelfCheck.RunAsync(fixture.Root, fixture.Platform, CancellationToken.None, fixture.Output);
        Assert.True(result.GroupPassed, string.Join("\n", result.Issues));
        Assert.Empty(result.UnobservedCapabilities);
        Assert.Empty(result.Issues);
        Assert.False(result.Checks.ContainsKey("hole_feature_regression_tests_passed"));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "output")));
        Assert.NotEmpty(Directory.GetFiles(fixture.Output, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MainWorkflowValidatesArtifactsUnderRequestedOutputDirectory()
    {
        using var fixture = new Fixture();
        var platform = fixture.Platform;
        var result = await new SolidWorksMainWorkflowRunner(platform.SkillRegistry, platform.WorkerRegistry,
            platform.AuditLog, platform.WorkflowEngine).ExecuteAsync(new SolidWorksMainWorkflowRequest(
                "isolated-output", "isolated-output", PlatformPathResolver.FindProjectRoot(), fixture.Output,
                DryRun: true, ModelSpec: SolidWorksWorkflowRouter.CreatePlateBasicFourHolesSpec()));
        Assert.Equal("Completed", result.Status);
        Assert.True(result.QualityGatePassed, string.Join("; ", result.Issues));
        Assert.False(result.RealCadExecuted);
        Assert.NotEmpty(result.ArtifactPaths);
        Assert.All(result.ArtifactPaths, path => Assert.StartsWith(Path.GetFullPath(fixture.Output), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("--output")]
    [InlineData("--unknown")]
    public void SelfCheckRejectsIncompleteOrUnknownOptions(string option) =>
        Assert.Throws<ArgumentException>(() => SelfCheckCliOptions.ResolveOutputRoot(["self-check", option], "project"));

    [Fact]
    public void SelfCheckOutputOptionPreservesDefaultAndResolvesExplicitPath()
    {
        Assert.Equal(Path.Combine("project", "output"), SelfCheckCliOptions.ResolveOutputRoot(["self-check"], "project"));
        Assert.Equal(Path.GetFullPath("custom-check"), SelfCheckCliOptions.ResolveOutputRoot(["self-check", "--output", "custom-check"], "project"));
    }

    private static AgentContext Context(Dictionary<string, string> values) => new(Guid.NewGuid().ToString("N"),
        new("test", "test", "test", "tester", "结构化建模请求", [], values), new Dictionary<string, object?>(), DateTimeOffset.UtcNow);

    private sealed class RejectAnyWorker : ISolidWorksWorker
    {
        public int InvocationCount { get; private set; }
        public string Name => "FakeSolidWorksWorker";
        public string TargetSystem => "SolidWorks";
        public Task<SolidWorksWorkerResult> ExecuteAsync(SolidWorksWorkerRequest request, CancellationToken cancellationToken = default)
        { InvocationCount++; throw new InvalidOperationException("无效输入不得调用 Worker。"); }
        public Task<WorkerOutput> ExecuteAsync(WorkerInput input) => ExecuteAsync(input, CancellationToken.None);
        public Task<WorkerOutput> ExecuteAsync(WorkerInput input, CancellationToken cancellationToken)
        { InvocationCount++; throw new InvalidOperationException("无效输入不得调用 Worker。"); }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cad-reliability-" + Guid.NewGuid().ToString("N"));
        public string Output => Path.Combine(Root, "isolated-output");
        public PlatformKernel Platform { get; }
        public Fixture()
        {
            var source = PlatformPathResolver.FindProjectRoot();
            Platform = PlatformBootstrapper.CreateDefault(source);
            Directory.CreateDirectory(Path.Combine(Root, "examples"));
            Directory.CreateDirectory(Path.Combine(Root, "docs"));
            foreach (var file in Directory.GetFiles(Path.Combine(source, "examples"), "hole_*_plate.json"))
                File.Copy(file, Path.Combine(Root, "examples", Path.GetFileName(file)));
            foreach (var file in new[] { "version_stage_index.md", "v2_1_b_hole_features.md", "self_check_capability_baseline.json" })
                File.Copy(Path.Combine(source, "docs", file), Path.Combine(Root, "docs", file));
        }
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("临时目录越界。");
            Directory.Delete(Root, true);
        }
    }
}
