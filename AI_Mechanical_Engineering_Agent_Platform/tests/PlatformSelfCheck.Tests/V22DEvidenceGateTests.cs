using System.Text.Json;
using DomainSchemas;
using PlatformCore.Modules.CADModeling.Validators;
using SolidWorksWorker;
using SolidWorksWorker.Features;

namespace PlatformSelfCheck.Tests;

public sealed class V22DEvidenceGateTests
{
    [Theory]
    [InlineData("shaft_basic")]
    [InlineData("SHAFT_BASIC")]
    [InlineData("jacket_basic")]
    [InlineData("JACKET_BASIC")]
    public void MissingExplicitEvidenceRootFailsClosedRegardlessOfFamilyCase(string family)
    {
        using var directory = new TestDirectory();
        var missingRoot = Path.Combine(directory.Root, "missing-evidence-root");
        Assert.False(Directory.Exists(missingRoot));
        Assert.True(PartFamilyProductionEvidencePolicy.RequiresEvidence(family));
        Assert.False(PartFamilyProductionEvidencePolicy.IsActive(family, missingRoot));
        var result = PartFamilyProductionEvidencePolicy.ValidateForRealExecution(Plan(family.ToLowerInvariant()) with { PartType = family }, missingRoot);
        Assert.False(result.IsValid);
        Assert.Equal(PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient, result.FailureStage);
        Assert.Contains(result.Issues, issue => issue.Contains("缺少零件族", StringComparison.Ordinal));
        var builders = PartFamilyBuilderRegistry.CreateDefault(evidenceRoot: missingRoot);
        Assert.True(builders.TryGetBuilder(family, out var builder));
        Assert.False(builder.SupportsRealExecution);
    }

    [Theory]
    [InlineData("shaft_basic", "unknown_parameter")]
    [InlineData("shaft_basic", "legacy_strategy")]
    [InlineData("shaft_basic", "uppercase_family")]
    [InlineData("jacket_basic", "unknown_parameter")]
    [InlineData("jacket_basic", "legacy_strategy")]
    [InlineData("jacket_basic", "uppercase_family")]
    public async Task UnboundParametersStrategyOrCaseCannotReachEnvironmentOrCom(string family, string mutation)
    {
        using var directory = new TestDirectory();
        var original = Plan(family);
        Assert.Equal(SolidWorksBuildExecutionStrategies.FeatureHandlerGraph, original.ExecutionStrategy);
        var changed = mutation switch
        {
            "unknown_parameter" => original with { Dimensions = new Dictionary<string, string>(original.Dimensions!) { ["unverified_parameter"] = "123" } },
            "legacy_strategy" => original with { ExecutionStrategy = SolidWorksBuildExecutionStrategies.PartFamilyBuilder },
            _ => original with { PartType = family.ToUpperInvariant() }
        };
        Assert.NotEqual(PartFamilyProductionEvidencePolicy.ComputePlanFingerprint(original), PartFamilyProductionEvidencePolicy.ComputePlanFingerprint(changed));

        // 仅替换 Handler 的物理证据读取和 Builder 能力声明，隔离本轮零件族门禁；执行入口始终抛错。
        var handlers = new FeatureHandlerRegistry(FeatureHandlerRegistry.CreateDefault().GetAll().Select(h => new ParameterOnlyHandler(h)));
        var session = new NoConnectionSession();
        var environment = new CountingEnvironment();
        var builder = new NonExecutingBuilder(family);
        var options = new SolidWorksRuntimeOptions(true, false, null, directory.Root, 1, 1,
            MainWorkflowExecutionEnabled: true, IsCiEnvironment: false, IsUnitTestEnvironment: false);
        var worker = new RealSolidWorksWorker(session, options, null, null, null, null,
            new PartFamilyBuilderRegistry([builder]), environment, handlers);
        var result = await worker.ExecuteAsync(new SolidWorksWorkerRequest("evidence-negative", changed, directory.Root, DryRun: false));

        Assert.Equal("Rejected", result.Status);
        Assert.Equal(PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient, result.FailureStage);
        Assert.False(result.RealCadConnected);
        Assert.False(result.RealCadExecuted);
        Assert.Equal(0, session.ConnectCount);
        Assert.Equal(0, environment.Count);
        Assert.Equal(0, builder.BuildCount);
        Assert.Contains(result.Logs, log => log.Contains("independent part-family CAD evidence", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("shaft_basic")]
    [InlineData("jacket_basic")]
    public void CompleteIndependentGeometryReportPassesTheArtifactContract(string family)
    {
        using var fixture = new ReportFixture(family);
        var review = fixture.Validate(fixture.Payload);
        Assert.True(review.IsPassed, string.Join(Environment.NewLine, review.Issues));
    }

    [Theory]
    [InlineData("shaft_basic", "missing_family")]
    [InlineData("shaft_basic", "unknown_family")]
    [InlineData("shaft_basic", "different_report_family")]
    [InlineData("shaft_basic", "different_plan_family")]
    [InlineData("jacket_basic", "missing_family")]
    [InlineData("jacket_basic", "unknown_family")]
    [InlineData("jacket_basic", "different_report_family")]
    [InlineData("jacket_basic", "different_plan_family")]
    public void ReportFamilyCannotHideOrReplaceGeometryPlanIdentity(string family, string mutation)
    {
        using var fixture = new ReportFixture(family);
        var payload = new Dictionary<string, object?>(fixture.Payload);
        switch (mutation)
        {
            case "missing_family": payload.Remove("part_type"); break;
            case "unknown_family": payload["part_type"] = "unregistered_family"; break;
            case "different_report_family": payload["part_type"] = PlateBasic4HolesDefinition.Type; break;
            default: payload["geometry_model_plan"] = fixture.Plan with { PartType = PlateBasic4HolesDefinition.Type }; break;
        }
        var review = fixture.Validate(payload);
        Assert.False(review.IsPassed);
        Assert.Contains(review.Issues, issue => issue.Contains("geometry_evidence_invalid", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("shaft_basic", "missing_plan")]
    [InlineData("shaft_basic", "missing_measured")]
    [InlineData("shaft_basic", "missing_circles")]
    [InlineData("shaft_basic", "not_declared")]
    [InlineData("shaft_basic", "not_attempted")]
    [InlineData("jacket_basic", "missing_plan")]
    [InlineData("jacket_basic", "missing_measured")]
    [InlineData("jacket_basic", "missing_circles")]
    [InlineData("jacket_basic", "not_declared")]
    [InlineData("jacket_basic", "not_attempted")]
    public void DetailedFamiliesCannotFallBackToDeclaredVolumeOrNotDeclaredStatus(string family, string mutation)
    {
        using var fixture = new ReportFixture(family);
        var payload = new Dictionary<string, object?>(fixture.Payload);
        switch (mutation)
        {
            case "missing_plan": payload.Remove("geometry_model_plan"); break;
            case "missing_measured": payload.Remove("measured_geometry"); break;
            case "missing_circles": payload["measured_geometry"] = fixture.Measured with { Edges = [] }; break;
            case "not_declared": payload["geometry_validation_status"] = "NotDeclared"; break;
            default: payload["geometry_validation_attempted"] = false; break;
        }
        var review = fixture.Validate(payload);
        Assert.False(review.IsPassed);
        var expectedIssue = mutation is "not_declared" ? "geometry_validation_status" : mutation is "not_attempted" ? "geometry_validation_attempted" : "geometry_evidence_invalid";
        Assert.Contains(review.Issues, issue => issue.Contains(expectedIssue, StringComparison.Ordinal));
    }

    private static SolidWorksBuildPlan Plan(string family)
    {
        var dimensions = family == ShaftBasicDefinition.Type
            ? new Dictionary<string, string> { ["diameter_mm"] = "40", ["length_mm"] = "180" }
            : new Dictionary<string, string> { ["outer_diameter_mm"] = "140", ["inner_diameter_mm"] = "120", ["length_mm"] = "180" };
        Assert.True(PartTypeRegistry.CreateDefault().TryGetDefinition(family, out var definition));
        var result = definition.GenerateBuildPlan("v22d-test", new("v22d-spec", family, dimensions));
        Assert.True(result.IsSuccess, string.Join("; ", result.Issues));
        return result.BuildPlan!;
    }

    private sealed class ReportFixture : IDisposable
    {
        private readonly TestDirectory _directory = new();
        private readonly SolidWorksWorkerResult _result;
        private readonly string _report;
        public SolidWorksBuildPlan Plan { get; }
        public MeasuredGeometry Measured { get; }
        public Dictionary<string, object?> Payload { get; }
        private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        public ReportFixture(string family)
        {
            Plan = V22DEvidenceGateTests.Plan(family);
            var shaft = family == ShaftBasicDefinition.Type;
            var volume = shaft ? Math.PI * 20 * 20 * 180 : Math.PI * (70 * 70 - 60 * 60) * 180;
            Measured = new(true, null, shaft ? new(0, -20, -20, 180, 20, 20) : new(-70, 0, -70, 70, 180, 70),
                1, volume, null, volume,
                Cylinders: shaft ? [new(40, 0, 0, 0, 1, 0, 0)] : [new(140, 0, 0, 0, 0, 1, 0), new(120, 0, 0, 0, 0, 1, 0)],
                Edges: shaft ? [Circle(0, true, 0, 20), Circle(1, true, 180, 20)] :
                    [Circle(0, false, 0, 70), Circle(1, false, 180, 70), Circle(2, false, 0, 60), Circle(3, false, 180, 60)]);

            // 报告格式替身只供 Validator 单测，不绑定生产 manifest，也不声明这些占位文件是真实 CAD。
            var diagnosticPath = Path.Combine(_directory.Root, "diagnostic.json");
            var feature = new
            {
                feature_id = "fixture-feature", feature_type = shaft ? "revolve_boss" : "extrude_boss",
                handler_name = "fixture@2.0-c.2", api_evidence_status = "verified",
                result_object_validated = true, rebuild_passed = true, geometry_change_validated = true,
                adapter_id = RealSolidWorksFeatureAdapter.AdapterIdentifier, adapter_version = RealSolidWorksFeatureAdapter.CurrentAdapterVersion,
                evidence_id = "fixture-only", evidence_handler_version = "2.0-c.2", evidence_parameter_profile = "fixture-only",
                evidence_solid_works_version = "31.5.0", evidence_diagnostic_run_path = diagnosticPath,
                evidence_source_revision = "fixture-only", failure_stage = (string?)null, issues = Array.Empty<string>()
            };
            Write("diagnostic.json", new
            {
                candidate_only = true, main_workflow_accepted = false, quality_gate_passed = false,
                solid_works_connected = true, real_cad_executed = true, final_status = "CandidatePassed",
                deliverable_status = "NotDeliverable", solid_works_version = "31.5.0", feature_handler_reports = new[] { feature }
            });
            Payload = new()
            {
                ["part_type"] = family, ["real_cad_executed"] = true, ["real_cad_connected"] = true,
                ["sldprt_save_success"] = true, ["step_export_success"] = true,
                ["execution_mode"] = PartFamilyExecutionModes.GenericFeatureGraph,
                ["execution_strategy"] = SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                ["final_status"] = "Passed", ["solidworks_version"] = "31.5.0", ["feature_handler_reports"] = new[] { feature },
                ["geometry_model_plan"] = Plan, ["measured_geometry"] = Measured,
                ["geometry_validation_attempted"] = true, ["geometry_validation_status"] = "Passed",
                ["expected_body_count"] = 1, ["measured_body_count"] = 1,
                ["expected_volume_cubic_mm"] = volume, ["measured_volume_cubic_mm"] = volume, ["geometry_volume_relative_tolerance"] = 0.01
            };
            _report = Write("build_report.json", Payload);
            var featureReport = Write("feature_execution_report.json", new
            {
                real_cad_executed = true, real_cad_connected = true, all_features_executed = true,
                all_result_objects_validated = true, all_rebuilds_passed = true, all_geometry_changes_validated = true,
                artifacts_validated = true, execution_mode = PartFamilyExecutionModes.GenericFeatureGraph,
                execution_strategy = SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                final_status = "Passed", solidworks_version = "31.5.0", feature_results = new[] { feature }
            });
            var part = Path.Combine(_directory.Root, "fixture.SLDPRT");
            var step = Path.Combine(_directory.Root, "fixture.STEP");
            File.WriteAllText(part, "仅供文件存在检查的替身");
            File.WriteAllText(step, "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n");
            _result = new("fixture", "Completed", new[] { part, step, _report, featureReport }.Select(path =>
                new SolidWorksArtifact(Path.GetFileName(path), "fixture", path, Path.GetExtension(path), true, new FileInfo(path).Length, "单测替身")).ToArray(),
                [], [], PartFamilyExecutionModes.GenericFeatureGraph, RealCadExecuted: true, RealCadConnected: true);
        }

        public ReviewReport Validate(Dictionary<string, object?> payload)
        {
            File.WriteAllText(_report, JsonSerializer.Serialize(payload, Options));
            return new SolidWorksArtifactValidator(_directory.Root).Validate(_result);
        }
        public void Dispose() => _directory.Dispose();
        private string Write(string name, object payload)
        {
            var path = Path.Combine(_directory.Root, name);
            File.WriteAllText(path, JsonSerializer.Serialize(payload, Options));
            return path;
        }
        private static MeasuredEdge Circle(int index, bool shaft, double position, double radius) => new(index, EdgeKinds.Circle,
            2 * Math.PI * radius, shaft ? position : 0, shaft ? 0 : position, 0, radius,
            [SurfaceKinds.Plane, SurfaceKinds.Cylinder], shaft ? new(1, 0, 0) : new(0, 1, 0));
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "v22d-gate-tests", Guid.NewGuid().ToString("N"), "real");
        public TestDirectory() => Directory.CreateDirectory(Root);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private sealed class ParameterOnlyHandler(IFeatureHandler inner) : FeatureHandlerBase
    {
        public override string FeatureType => inner.FeatureType;
        public override string OperationType => ((FeatureHandlerBase)inner).OperationType;
        public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema => inner.ParameterSchema;
        public override FeatureApiEvidence ApiEvidence => inner.ApiEvidence;
        public override FeatureHandlerValidationResult Validate(FeatureDefinition feature) => inner.Validate(feature);
        public override FeatureHandlerValidationResult ValidateEvidenceForRealExecution(FeatureDefinition feature) => inner.ValidateParameterProfileForRealExecution(feature);
        public override Task<FeatureHandlerExecutionResult> ExecuteAsync(FeatureHandlerExecutionContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("不应执行测试 Handler。");
    }

    private sealed class NonExecutingBuilder(string family) : IPartFamilyBuilder
    {
        public string PartType => family;
        public string FailureStage => PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient;
        public bool SupportsRealExecution => true;
        public string ApiEvidence => "仅隔离零件族证据门禁的测试替身";
        public string RealExecutionMode => PartFamilyExecutionModes.GenericFeatureGraph;
        public int BuildCount { get; private set; }
        public Task<PartFamilyDryRunBuildResult> BuildDryRunAsync(SolidWorksWorkerRequest request, string artifactsDirectory, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("不应执行 dry-run。");
        public Task<PartFamilyBuildResult> BuildAsync(PartFamilyBuildContext context, CancellationToken cancellationToken = default)
        { BuildCount++; throw new InvalidOperationException("不应执行 Builder。"); }
    }

    private sealed class CountingEnvironment : ISolidWorksExecutionEnvironmentProbe
    {
        public int Count { get; private set; }
        public SolidWorksExecutionEnvironmentProbeResult Probe() { Count++; return new(true, []); }
    }

    private sealed class NoConnectionSession : ISolidWorksSessionManager
    {
        public int ConnectCount { get; private set; }
        public Task<SolidWorksSessionConnectionResult> ConnectAsync(SolidWorksRuntimeOptions options, CancellationToken cancellationToken = default)
        { ConnectCount++; throw new InvalidOperationException("不应连接 COM。"); }
        public Task<T> ExecuteWithApplicationAsync<T>(Func<object, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default, int? executionTimeoutSeconds = null) =>
            throw new InvalidOperationException("不应执行 COM。");
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
