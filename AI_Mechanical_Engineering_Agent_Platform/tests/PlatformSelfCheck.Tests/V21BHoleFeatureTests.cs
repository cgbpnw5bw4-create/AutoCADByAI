using System.Text.Json;
using DomainSchemas;
using PlatformCore;
using PlatformCore.Modules.CADModeling.Skills;
using PlatformCore.Modules.CADModeling.Validators;
using SkillContracts;
using SolidWorksWorker;
using SolidWorksWorker.Features;
using SolidWorksWorker.Features.Hole;

namespace PlatformSelfCheck.Tests;

public sealed class V21BHoleFeatureTests
{
    [Theory]
    [InlineData("simple")]
    [InlineData("counterbore")]
    [InlineData("countersink")]
    [InlineData("tapped")]
    public async Task ExamplesPassSkillCompilePreWorkerValidationAndFakeWorker(string name)
    {
        var spec = Load(name);
        Assert.Equal("true", spec.ExecutionOptions["dry_run"]);
        var skill = await new SolidWorksBuildPlanSkill().ExecuteAsync(new SkillInput("v21-b", "hole", spec, new Dictionary<string, string>()));
        Assert.Equal(SkillOutputStatus.Completed, skill.Status);
        var compiled = new BuildPlanCompiler().Compile(spec);
        Assert.True(compiled.IsSuccess, string.Join(" | ", compiled.Issues));
        var plan = Assert.IsType<SolidWorksBuildPlan>(skill.Result);
        Assert.True(new SolidWorksBuildPlanValidator().Validate(plan).IsPassed);
        var result = await new FakeSolidWorksWorker().ExecuteAsync(new SolidWorksWorkerRequest("v21-b", plan,
            Path.Combine(Root(), "output", "tests", "v21_b", name), DryRun: true));
        Assert.True(result.Status == "Completed", string.Join(" | ", result.Issues));
        Assert.False(result.RealCadConnected);
        Assert.False(result.RealCadExecuted);
        var hole = spec.Features.Last();
        var handler = new HoleHandler();
        var planned = handler.BuildPlan(hole, new("hole-op", "TopPlane", ["boss"]));
        Assert.Equal("CreateHole", planned.Operation!.OperationType);
        var report = handler.GenerateReport(hole, FeatureHandlerExecutionResult.Passed());
        Assert.Equal(hole.Parameters["hole_type"], report.HoleType);
        Assert.Equal("unverified", report.ApiEvidenceStatus);
        Assert.Null(report.EvidenceSourceRevision);
        Assert.Equal(new HoleValidator().Validate(hole).Definition!.ToParameters(), report.HoleParameters);
    }

    [Theory]
    [InlineData("hole_type", "OtherHole", "unsupported_hole_type")]
    [InlineData("diameter_mm", "0", "invalid_hole_parameter")]
    [InlineData("diameter_mm", "NaN", "invalid_hole_parameter")]
    [InlineData("diameter_mm", "Infinity", "invalid_hole_parameter")]
    [InlineData("depth_mm", "0", "invalid_hole_parameter")]
    [InlineData("depth_mm", "20", "invalid_hole_parameter")]
    [InlineData("depth_mm", "30", "invalid_hole_parameter")]
    [InlineData("through_all", "maybe", "invalid_hole_parameter")]
    [InlineData("reference_face", "", "hole_reference_face_missing")]
    [InlineData("reference_face", "madeup:end_face", "hole_reference_face_missing")]
    [InlineData("reference_face", "plate_boss:arbitrary", "hole_reference_face_missing")]
    [InlineData("position", "{}", "invalid_hole_parameter")]
    [InlineData("position", "{\"x_mm\":49,\"y_mm\":0}", "invalid_hole_parameter")]
    [InlineData("position", "{\"x_mm\":0,\"y_mm\":0,\"z_mm\":1}", "invalid_hole_parameter")]
    [InlineData("quantity", "2", "invalid_hole_parameter")]
    [InlineData("quantity", "1.5", "invalid_hole_parameter")]
    [InlineData("pattern_reference", "unknown", "invalid_hole_parameter")]
    [InlineData("thread_size", "M6", "invalid_hole_parameter")]
    public void InvalidSimpleHoleIsRejectedBeforeWorker(string key, string value, string stage)
    {
        var spec = Change(Load("simple"), key, value);
        var result = new BuildPlanCompiler().Compile(spec);
        Assert.False(result.IsSuccess);
        Assert.Equal(stage, result.FailureStage);
    }

    [Theory]
    [InlineData("counterbore", "counterbore_diameter_mm", "5")]
    [InlineData("counterbore", "counterbore_depth_mm", "0")]
    [InlineData("counterbore", "counterbore_depth_mm", "12")]
    [InlineData("counterbore", "position", "{\"x_mm\":46,\"y_mm\":0}")]
    [InlineData("countersink", "countersink_diameter_mm", "6")]
    [InlineData("countersink", "countersink_angle_deg", "0")]
    [InlineData("countersink", "countersink_angle_deg", "180")]
    [InlineData("countersink", "countersink_angle_deg", "NaN")]
    [InlineData("countersink", "countersink_angle_deg", "1")]
    [InlineData("tapped", "thread_standard", "unknown")]
    [InlineData("tapped", "thread_size", "M7")]
    [InlineData("tapped", "thread_pitch", "2")]
    [InlineData("tapped", "thread_depth_mm", "13")]
    [InlineData("tapped", "tap_drill_diameter_mm", "6")]
    [InlineData("tapped", "diameter_mm", "6")]
    public void TypeSpecificIllegalParametersAreRejected(string name, string key, string value) =>
        Assert.Equal(PartFamilyFailureStages.InvalidHoleParameter, new BuildPlanCompiler().Compile(Change(Load(name), key, value)).FailureStage);

    [Fact]
    public void TappedDefinitionRejectsTinyDrillEvenWhenAliasesMatch()
    {
        var spec = Change(Change(Load("tapped"), "diameter_mm", "0.01"), "tap_drill_diameter_mm", "0.01");
        Assert.False(new BuildPlanCompiler().Compile(spec).IsSuccess);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("counterbore")]
    [InlineData("countersink")]
    [InlineData("tapped")]
    public void ThroughHoleMayOmitDepthAndStillChecksMaterialDepth(string name)
    {
        var spec = Change(Load(name), "through_all", "true");
        var p = new Dictionary<string, string>(spec.Features.Last().Parameters); p.Remove("depth_mm");
        spec = Replace(spec, spec.Features.Last() with { Parameters = p });
        Assert.True(new BuildPlanCompiler().Compile(spec).IsSuccess);
        if (name == "tapped") Assert.False(new BuildPlanCompiler().Compile(Change(spec, "thread_depth_mm", "21")).IsSuccess);
    }

    [Theory]
    [InlineData("hole")]
    [InlineData("HOLE")]
    [InlineData("HoLe")]
    public void CaseInsensitiveFeatureTypeCannotBypassFaceValidation(string type)
    {
        var spec = Change(Load("simple"), "reference_face", "missing:end_face");
        spec = Replace(spec, spec.Features.Last() with { FeatureType = type });
        Assert.Equal(PartFamilyFailureStages.HoleReferenceFaceMissing, new BuildPlanCompiler().Compile(spec).FailureStage);
    }

    [Theory]
    [InlineData("feature_type")]
    [InlineData("hole_type")]
    public async Task DeletingCompiledIdentityCannotBypassPlanOrFakeValidation(string deleted)
    {
        var plan = new BuildPlanCompiler().Compile(Load("simple") with { RequiresFeatureHandlerPipeline = true }).BuildPlan!;
        var operations = plan.Operations.ToArray(); var index = Array.FindIndex(operations, o => o.OperationType == "CreateHole");
        var p = new Dictionary<string, string>(operations[index].Parameters); p.Remove(deleted); p["reference_face"] = "bad:end_face";
        operations[index] = operations[index] with { Parameters = p };
        plan = plan with { Operations = operations };
        Assert.False(new SolidWorksBuildPlanValidator().Validate(plan).IsPassed);
        Assert.False(FeatureHandlerRegistry.CreateDefault().ValidateForRealExecution(plan).IsPassed);
        var fake = await new FakeSolidWorksWorker().ExecuteAsync(new SolidWorksWorkerRequest("tamper", plan, Path.Combine(Root(), "output", "tests", "v21_b", "tamper")));
        Assert.Equal("Rejected", fake.Status);
    }

    [Fact]
    public void UnknownPlaneConstructionAndAmbiguousRectangleCannotSupplyMaterial()
    {
        var spec = Load("simple"); var sketch = spec.Sketches[0];
        Assert.False(new BuildPlanCompiler().Compile(spec with { Sketches = [sketch with { ReferencePlane = "MadeUp" }] }).IsSuccess);
        Assert.False(new BuildPlanCompiler().Compile(spec with { Sketches = [sketch with { Entities = [sketch.Entities[0] with { Construction = true }] }] }).IsSuccess);
        var p = new Dictionary<string, string>(sketch.Entities[0].Parameters) { ["length_mm"] = "20" };
        Assert.False(new BuildPlanCompiler().Compile(spec with { Sketches = [sketch with { Entities = [sketch.Entities[0] with { Parameters = p }] }] }).IsSuccess);
    }

    [Fact]
    public void CrossFeatureOverlapOnEitherFaceIsRejected()
    {
        var spec = Load("simple");
        foreach (var face in new[] { "plate_boss:end_face", "plate_boss:start_face" })
        {
            var other = Change(spec, "reference_face", face).Features.Last() with { FeatureId = "second", ExecutionOrder = 3 };
            Assert.False(new BuildPlanCompiler().Compile(spec with { Features = [.. spec.Features, other] }).IsSuccess);
        }
    }

    [Fact]
    public void PatternReferenceMustResolveToTheSameExplicitPointSet()
    {
        var spec = Change(Change(Load("simple"), "quantity", "2"), "position", "[{\"x_mm\":-15,\"y_mm\":0},{\"x_mm\":15,\"y_mm\":0}]");
        spec = Change(spec, "pattern_reference", "sketch:points");
        var points = new SketchDefinition("points", "TopPlane", new[] { -15, 15 }.Select((x, i) => new SketchEntity("p" + i, "circle",
            new Dictionary<string, string> { ["center_x_mm"] = x.ToString(), ["center_y_mm"] = "0", ["radius_mm"] = "3" })).ToArray());
        spec = Replace(spec with { Sketches = [.. spec.Sketches, points] }, spec.Features.Last() with { ReferencedSketches = ["points"] });
        var plan = new BuildPlanCompiler().Compile(spec);
        Assert.True(plan.IsSuccess, string.Join(" | ", plan.Issues));
        Assert.True(new SolidWorksBuildPlanValidator().Validate(plan.BuildPlan!).IsPassed);
        Assert.False(new BuildPlanCompiler().Compile(spec with { Sketches = [spec.Sketches[0], points with { ReferencePlane = "FrontPlane" }] }).IsSuccess);
        Assert.False(new BuildPlanCompiler().Compile(Change(spec, "position", "[{\"x_mm\":-14,\"y_mm\":0},{\"x_mm\":15,\"y_mm\":0}]")).IsSuccess);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("counterbore")]
    [InlineData("countersink")]
    [InlineData("tapped")]
    public async Task EveryExplicitHoleIsBlockedInHandlerCandidateProfileAndAdapterBeforeCom(string name)
    {
        var feature = Load(name).Features.Last(); var handler = new HoleHandler();
        Assert.False(handler.ValidateParameterProfileForRealExecution(feature).IsValid);
        Assert.False(handler.ValidateEvidenceForRealExecution(feature).IsValid);
        var com = new NoCom();
        using var adapter = new RealSolidWorksFeatureAdapter(new object(), com);
        var result = await adapter.ExecuteHoleAsync(feature, new("hole", "CreateHole", "TopPlane", feature.Parameters, [], ""), new());
        Assert.False(result.IsSuccess); Assert.Equal(0, com.Calls);
        Assert.Equal(name == "tapped" ? "tapped_hole_api_unverified" : "feature_api_unverified", result.FailureStage);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("counterbore")]
    [InlineData("countersink")]
    [InlineData("tapped")]
    public void MeasuredHoleChecksRejectMissingCountDiameterDepthAndType(string name)
    {
        var spec = Load(name); var measured = Measurement(name);
        var valid = Validate(spec, measured);
        Assert.Equal("Passed", valid.FinalStatus);
        Assert.Equal("Failed", Validate(spec, measured with { Holes = null }).FinalStatus);
        Assert.Equal("Failed", Validate(spec, measured with { Holes = [] }).FinalStatus);
        Assert.Equal("Failed", Validate(spec, measured with { Holes = [measured.Holes![0], measured.Holes[0]] }).FinalStatus);
        foreach (var invalid in new[] { measured.Holes![0] with { DiameterMm = 99 }, measured.Holes[0] with { DepthMm = 1 },
            measured.Holes[0] with { Position = new(20, 0) }, measured.Holes[0] with { HoleType = "fake" } })
            Assert.Equal(PartFamilyFailureStages.HoleGeometryValidationFailed, Validate(spec, measured with { Holes = [invalid] }).FailureStage);
    }

    [Fact]
    public void TappedWizardMetadataIsNotOrdinaryCutCosmeticOnlyOrModeledThread()
    {
        var spec = Load("tapped"); var measured = Measurement("tapped"); var hole = measured.Holes![0];
        foreach (var metadata in new MeasuredTappedHole?[] { null, hole.Tapped! with { Representation = "geometric_cut" },
            hole.Tapped! with { Representation = "cosmetic_thread" }, hole.Tapped! with { ModeledThreadPresent = true },
            hole.Tapped! with { ThreadSize = "M8" }, hole.Tapped! with { ThreadPitch = 2 } })
            Assert.Equal("Failed", Validate(spec, measured with { Holes = [hole with { Tapped = metadata }] }).FinalStatus);
        Assert.Equal("Failed", Validate(spec, measured with { Features = [new("Sketch1", "ProfileFeature", 0), new("Boss1", "Extrusion", 0), new("Hole1", "ICE", 0)] }).FinalStatus);
        Assert.Equal("Failed", Validate(spec, measured with { Features = [new("Sketch1", "ProfileFeature", 0), new("Boss1", "Extrusion", 0), new("Hole1", "HoleWzd", null)] }).FinalStatus);
    }

    [Fact]
    public void StepAndConeExistenceCannotBeReplacedByMatchingNumbers()
    {
        var cb = Measurement("counterbore");
        Assert.Equal("Failed", Validate(Load("counterbore"), cb with { Holes = [cb.Holes![0] with { CoaxialStepsVerified = false }] }).FinalStatus);
        var cs = Measurement("countersink");
        Assert.Equal("Failed", Validate(Load("countersink"), cs with { Holes = [cs.Holes![0] with { ConeSurfaceVerified = false }] }).FinalStatus);
    }

    [Fact]
    public void DryRunCliForcesSafeModeDespiteContradictoryInput()
    {
        var context = CadDryRunCliContract.ForceDryRun(new Dictionary<string, string> { ["dry_run"] = "false", ["operation"] = "build_part_family_release_package", ["generate_release_package"] = "true" });
        Assert.Equal("true", context["dry_run"]); Assert.Equal("false", context["generate_release_package"]); Assert.Equal("build_plate", context["operation"]);
    }

    [Fact]
    public void GeometryReportCannotSubstituteDifferentHoleDefinitions()
    {
        var tapped = Load("tapped");
        var h = new HoleValidator().Validate(tapped.Features.Last()).Definition!;
        var reports = new[] { new HoleReportedDefinition("test_hole", h.HoleType, h.ToParameters()) };
        Assert.True(HoleGeometryValidation.ReportDefinitionsMatch(tapped, reports));
        Assert.False(HoleGeometryValidation.ReportDefinitionsMatch(Load("simple"), reports));
        Assert.False(HoleGeometryValidation.ReportDefinitionsMatch(Change(tapped, "thread_depth_mm", "9"), reports));
        Assert.False(HoleGeometryValidation.ReportDefinitionsMatch(tapped, [reports[0], reports[0]]));
    }

    [Fact]
    public void ArtifactValidatorRejectsTypedGeometryWhenHandlerHoleParametersAreDeleted()
    {
        var directory = Path.Combine(Root(), "output", "solidworks", "real", "v21_b_report_negative", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var spec = Load("tapped");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        // 故意保持旧式 Handler 报告（没有 hole_parameters），同时附加合法增强孔几何。
        var payload = new
        {
            real_cad_executed = true, real_cad_connected = true, sldprt_save_success = true, step_export_success = true,
            execution_mode = PartFamilyExecutionModes.GenericFeatureGraph,
            execution_strategy = SolidWorksBuildExecutionStrategies.FeatureHandlerGraph, final_status = "Passed",
            geometry_validation_status = "NotDeclared",
            feature_handler_reports = FeatureExecutionReportFixture.FeatureResults("sketch", "extrude_boss", "hole"),
            hole_model_spec = spec, hole_geometry_validation = Validate(spec, Measurement("tapped"))
        };
        var path = Path.Combine(directory, "build_report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, options));
        var artifact = new SolidWorksArtifact("report", "BuildReport", path, ".json", true, new FileInfo(path).Length, "测试报告");
        var result = new SolidWorksArtifactValidator().Validate(new SolidWorksWorkerResult("report-negative", "Completed", [artifact], [], [],
            PartFamilyExecutionModes.GenericFeatureGraph, RealCadExecuted: true, RealCadConnected: true));
        Assert.False(result.IsPassed);
        Assert.Contains(result.Issues, issue => issue.Contains("hole_geometry_validation_failed", StringComparison.Ordinal));
    }

    internal static CADModelSpec Load(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "examples", $"hole_{name}_plate.json")));
        return doc.RootElement.GetProperty("cad_model_spec").Deserialize<CADModelSpec>()!;
    }
    internal static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }
    internal static CADModelSpec Change(CADModelSpec spec, string key, string value) => Replace(spec, spec.Features.Last() with
    { Parameters = new Dictionary<string, string>(spec.Features.Last().Parameters, StringComparer.OrdinalIgnoreCase) { [key] = value } });
    internal static CADModelSpec Replace(CADModelSpec spec, FeatureDefinition hole) => spec with { Features = [.. spec.Features.SkipLast(1), hole] };
    internal static GeometryValidationReport Validate(CADModelSpec spec, MeasuredGeometry measurement) =>
        new GeometryValidator().Validate(spec, measurement, ["sketch", FeatureTypes.ExtrudeBoss, FeatureTypes.Hole]);
    internal static MeasuredGeometry Measurement(string name)
    {
        var type = new Dictionary<string, string> { ["simple"] = HoleTypes.Simple, ["counterbore"] = HoleTypes.Counterbore, ["countersink"] = HoleTypes.Countersink, ["tapped"] = HoleTypes.Tapped }[name];
        var removed = new Dictionary<string, double> { ["simple"] = 108 * Math.PI, ["counterbore"] = 216 * Math.PI, ["countersink"] = 144 * Math.PI, ["tapped"] = 75 * Math.PI }[name];
        var hole = new MeasuredHole("test_hole", "Hole1", type, "plate_boss:end_face", new(0, 0), name == "tapped" ? 5 : 6, 12, false,
            name == "counterbore" ? 12 : null, name == "counterbore" ? 4 : null,
            name == "countersink" ? 12 : null, name == "countersink" ? 90 : null,
            CoaxialStepsVerified: name == "counterbore", ConeSurfaceVerified: name == "countersink",
            Tapped: name == "tapped" ? new("hole_wizard_tapped", "ISO_METRIC", "M6", 1, 10, 5, true, false) : null);
        var box = new GeometryBoundingBox(-50, 0, -30, 50, 20, 30);
        return new(true, box, box, 1, 120000 - removed, null, 120000 - removed,
            FeatureTypes: ["ProfileFeature", "Extrusion", name == "tapped" ? "HoleWzd" : "ICE"],
            Features: [new("Sketch1", "ProfileFeature", 0), new("Boss1", "Extrusion", 0), new("Hole1", name == "tapped" ? "HoleWzd" : "ICE", 0)], Holes: [hole]);
    }
    private sealed class NoCom : ISolidWorksComFacade
    {
        public int Calls { get; private set; }
        private object Touch() { Calls++; throw new InvalidOperationException("禁止 COM 调用。"); }
        public object GetProperty(object t, string n) => Touch();
        public object? TryGetProperty(object? t, string n) => Touch();
        public object? TryGetIndexedProperty(object t, string n, params object?[] a) => Touch();
        public object? Invoke(object t, string n, params object?[] a) => Touch();
        public object? InvokeWithArgs(object t, string n, object?[] a) => Touch();
        public object? TryInvoke(object? t, string n, params object?[] a) => Touch();
        public object? TryInvokeWithArgs(object? t, string n, object?[] a) => Touch();
        public bool TryInvokeBool(object? t, string n, params object?[] a) => (bool)Touch();
        public bool TrySetProperty(object t, string n, object? v) => (bool)Touch();
        public bool TryExtensionSaveAs(object m, string p, object? d, List<string> e, List<string> w) => (bool)Touch();
        public void ReleaseComObject(object v) => Touch();
    }
}
