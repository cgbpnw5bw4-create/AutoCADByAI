using System.Text.Json;
using DomainSchemas;
using SolidWorksWorker;
using SolidWorksWorker.Features;
using SolidWorksWorker.Features.Revolve;
using SolidWorksWorker.Features.Sketch;

namespace PlatformSelfCheck.Tests;

public sealed class V22DRevolveAdapterTests
{
    [Fact]
    public void ShaftFactoryKeepsLegacyMetadataAndEmitsExplicitClosedCoordinates()
    {
        var plan = Plan();
        var profile = Assert.Single(plan.Operations, operation => operation.OperationType == "CreateSketch");
        Assert.Equal("closed_half_section", profile.Parameters["profile"]);
        Assert.Equal("40", profile.Parameters["diameter_mm"]);
        Assert.Equal("180", profile.Parameters["length_mm"]);
        Assert.Single(plan.Operations, operation => operation.OperationType == "CreateCenterLine");
        var handler = new SketchHandler();
        foreach (var operation in plan.Operations.Where(operation => operation.OperationType is "CreateSketch" or "CreateCenterLine"))
        {
            var validation = handler.ValidateParameterProfileForRealExecution(Adapt(operation));
            Assert.True(validation.IsValid, string.Join("; ", validation.Issues));
        }
        var entities = JsonSerializer.Deserialize<SketchEntity[]>(profile.Parameters["entities"])!;
        Assert.Equal(8, entities.Count(entity => entity.EntityType == SketchEntityTypes.Line));
        Assert.Single(entities, entity => entity.IsConstructionCenterLine);
        Assert.All(entities, entity =>
        {
            Assert.True(entity.Parameters.ContainsKey("x1_mm"));
            Assert.True(entity.Parameters.ContainsKey("x2_mm"));
        });
    }

    [Fact]
    public void JacketFactoryUsesExistingExecutableTopPlaneProfileWithoutHiddenConstraints()
    {
        var spec = PartFamilyGenericModelFactory.CreateJacketBasic(new CADModelSpec("jacket", "jacket_basic",
            new Dictionary<string, string> { ["outer_diameter_mm"] = "140", ["inner_diameter_mm"] = "120", ["length_mm"] = "180" }));
        Assert.All(spec.Sketches, sketch =>
        {
            Assert.Equal("TopPlane", sketch.ReferencePlane);
            Assert.Empty(sketch.Constraints);
            Assert.Empty(sketch.Dimensions);
        });
        Assert.Equal("70", Assert.Single(spec.Sketches[0].Entities).Parameters["radius_mm"]);
        Assert.Equal("60", Assert.Single(spec.Sketches[1].Entities).Parameters["radius_mm"]);
        Assert.Equal("180", spec.Features[0].Parameters["depth_mm"]);
        Assert.Equal("360", spec.Features[1].Parameters["depth_mm"]);
        var plan = new BuildPlanCompiler().Compile("jacket", spec).BuildPlan!;
        foreach (var operation in plan.Operations.Where(operation => operation.OperationType == "CreateSketch"))
            Assert.True(new SketchHandler().ValidateParameterProfileForRealExecution(Adapt(operation)).IsValid);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("no_axis")]
    [InlineData("many_axes")]
    [InlineData("offset_axis")]
    [InlineData("axis_mark")]
    [InlineData("not_construction")]
    [InlineData("nonfinite")]
    [InlineData("false_metadata")]
    public void RightPlaneProfileRejectsUnexecutableOrContradictoryGeometry(string mutation)
    {
        var operation = Plan().Operations[0];
        var parameters = operation.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
        var entities = JsonSerializer.Deserialize<SketchEntity[]>(parameters["entities"])!.ToList();
        void Change(int index, string key, string value)
        {
            var changed = entities[index].Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
            changed[key] = value;
            entities[index] = entities[index] with { Parameters = changed };
        }
        switch (mutation)
        {
            case "open": Change(entities.Count - 2, "x2_mm", "1"); break;
            case "no_axis": entities.RemoveAt(entities.Count - 1); break;
            case "many_axes": entities.Add(entities[^1] with { EntityId = "other_axis" }); break;
            case "offset_axis": Change(entities.Count - 1, "y1_mm", "1"); break;
            case "axis_mark": Change(entities.Count - 1, "selection_mark", "4"); break;
            case "not_construction": entities[^1] = entities[^1] with { Construction = false }; break;
            case "nonfinite": Change(0, "x1_mm", "NaN"); break;
            case "false_metadata": parameters["diameter_mm"] = "41"; break;
        }
        parameters["entities"] = JsonSerializer.Serialize(entities);
        parameters["entity_count"] = entities.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = new SketchHandler().Validate(Adapt(operation with { Parameters = parameters }));
        Assert.False(result.IsValid);
        Assert.Equal(PartFamilyFailureStages.InvalidFeatureParameter, result.FailureStage);
    }

    [Theory]
    [InlineData("angle_degrees", "180")]
    [InlineData("angle_degrees", "NaN")]
    [InlineData("axis_selection_mark", "4")]
    [InlineData("profile_selection_mark", "1")]
    [InlineData("thin", "true")]
    [InlineData("feature_api", "UnknownApi")]
    public async Task InvalidRevolveParametersNeverReachTheAdapterComBoundary(string key, string value)
    {
        var operation = Plan().Operations[2];
        var parameters = operation.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
        parameters[key] = value;
        operation = operation with { Parameters = parameters };
        var com = new RevolveComFacade();
        using var adapter = new RealSolidWorksFeatureAdapter(com.Model, com);
        var result = await adapter.ExecuteRevolveBossAsync(Adapt(operation), operation, new());
        Assert.False(result.IsSuccess);
        Assert.Empty(com.Calls);
    }

    [Fact]
    public async Task AdapterUsesExactProfileAxisAndTwentyArgumentsThenReadsTheResult()
    {
        var com = new RevolveComFacade();
        var result = await Execute(com);
        Assert.True(result.IsSuccess, string.Join("; ", result.Issues));
        Assert.Equal(1, com.Calls.Count(call => call == "CreateCenterLine"));
        Assert.Contains("FeatureByPositionReverse", com.Calls);
        Assert.Contains("GetSpecificFeature2", com.Calls);
        Assert.DoesNotContain("GetFeature", com.Calls);
        Assert.Equal(1, com.Calls.Count(call => call == "FeatureRevolve2"));
        Assert.Equal(20, com.RevolveArguments!.Length);
        Assert.Equal<object?[]>(
            [true, true, false, false, false, false, 0, 0, 2d * Math.PI, 0d, false, false, 0.01d, 0.01d, 0, 0d, 0d, true, true, true],
            com.RevolveArguments);
        Assert.Equal(2d * Math.PI, Assert.IsType<double>(com.RevolveArguments[8]));
        Assert.IsType<double>(com.RevolveArguments[15]);
        Assert.IsType<double>(com.RevolveArguments[16]);
        Assert.Equal(1, com.ProfileSelections);
        Assert.Equal(1, com.AxisSelections);
        var artifact = Assert.IsType<FeatureAdapterArtifact>(result.CreatedObject);
        Assert.True(artifact.GeometryChangeValidated);
        Assert.True(artifact.RebuildPassed);
        Assert.True(artifact.VolumeAfterCubicMeters > artifact.VolumeBeforeCubicMeters);
        Assert.Contains(result.Logs, log => log.Contains("revolve_angle_readback_radians", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("null_line")]
    [InlineData("null_axis")]
    [InlineData("axis_not_construction")]
    [InlineData("null_profile")]
    [InlineData("wrong_profile_type")]
    [InlineData("null_specific_sketch")]
    [InlineData("different_specific_sketch")]
    [InlineData("wrong_mark")]
    [InlineData("extra_selection")]
    [InlineData("null_revolve")]
    [InlineData("wrong_feature_type")]
    [InlineData("wrong_angle")]
    [InlineData("thin_result")]
    [InlineData("cut_result")]
    [InlineData("two_bodies")]
    [InlineData("no_volume")]
    [InlineData("rebuild_failed")]
    public async Task NullOrIncorrectCadResultsNeverBecomeSuccessfulArtifacts(string scenario)
    {
        var result = await Execute(new RevolveComFacade(scenario));
        Assert.False(result.IsSuccess);
        Assert.Null(result.CreatedObject);
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RevolveNeedsBothDirectProfileDependencyAndItsAxisConfirmation(bool omitConfirmation)
    {
        var plan = Plan();
        var com = new RevolveComFacade();
        using var adapter = new RealSolidWorksFeatureAdapter(com.Model, com);
        var profile = plan.Operations[0];
        Assert.True((await adapter.ExecuteSketchAsync(Adapt(profile), profile, new())).IsSuccess);
        if (!omitConfirmation)
        {
            var center = plan.Operations[1];
            Assert.True((await adapter.ExecuteSketchAsync(Adapt(center), center, new())).IsSuccess);
        }
        var revolve = plan.Operations[2];
        if (!omitConfirmation) revolve = revolve with { DependsOn = [plan.Operations[1].OperationId] };
        Assert.False((await adapter.ExecuteRevolveBossAsync(Adapt(revolve), revolve, new())).IsSuccess);
        Assert.DoesNotContain("FeatureRevolve2", com.Calls);
    }

    [Fact]
    public void RightPlaneReportDoesNotReuseTopPlaneEvidence()
    {
        var handler = new SketchHandler();
        var feature = Adapt(Plan().Operations[0]);
        var report = handler.GenerateReport(feature, FeatureHandlerExecutionResult.Failed("test", "测试"));
        Assert.Equal(handler.RightPlaneApiEvidence.ParameterProfile, report.EvidenceParameterProfile);
        Assert.NotEqual(handler.ApiEvidence.ParameterProfile, report.EvidenceParameterProfile);
    }

    private static async Task<FeatureHandlerExecutionResult> Execute(RevolveComFacade com)
    {
        using var adapter = new RealSolidWorksFeatureAdapter(com.Model, com);
        foreach (var operation in Plan().Operations.Where(operation => operation.OperationType is "CreateSketch" or "CreateCenterLine" or "RevolveBoss"))
        {
            var result = operation.OperationType == "RevolveBoss"
                ? await adapter.ExecuteRevolveBossAsync(Adapt(operation), operation, new())
                : await adapter.ExecuteSketchAsync(Adapt(operation), operation, new());
            if (!result.IsSuccess || operation.OperationType == "RevolveBoss") return result;
        }
        throw new InvalidOperationException("缺少旋转步骤。");
    }

    private static SolidWorksBuildPlan Plan() => new BuildPlanCompiler().Compile("shaft",
        PartFamilyGenericModelFactory.CreateShaftBasic(new CADModelSpec("shaft", "shaft_basic",
            new Dictionary<string, string>
            {
                ["diameter_mm"] = "40", ["length_mm"] = "180",
                ["optional_step_diameters"] = "32,24", ["optional_step_lengths"] = "40,30"
            }))).BuildPlan!;

    private static FeatureDefinition Adapt(SolidWorksOperation operation) => FeatureHandlerPlanAdapter.Adapt(operation).Feature!;

    // 只模拟接口返回和选择状态；不创建 COM 对象，也不写 CAD 文件。
    private sealed class RevolveComFacade(string scenario = "") : ISolidWorksComFacade
    {
        private readonly object _manager = new(), _sketch = new(), _profile = new(), _axis = new();
        private readonly object _selectData = new(), _feature = new(), _definition = new(), _body = new();
        private bool _axisSelected;
        private double _volume;
        private int _mark;
        public object Model { get; } = new();
        public List<string> Calls { get; } = [];
        public object?[]? RevolveArguments { get; private set; }
        public int ProfileSelections { get; private set; }
        public int AxisSelections { get; private set; }

        public object GetProperty(object target, string name) => TryGetProperty(target, name)!;
        public object? TryGetProperty(object? target, string name) => name switch
        {
            "SketchManager" or "SelectionManager" or "FeatureManager" or "Extension" => _manager,
            "ActiveSketch" => _sketch,
            "ConstructionGeometry" => scenario != "axis_not_construction",
            "Mark" => scenario == "wrong_mark" ? 4 : _mark,
            _ => null
        };
        public object? TryGetIndexedProperty(object target, string name, params object?[] args) => null;
        public object? Invoke(object target, string name, params object?[] args) => InvokeWithArgs(target, name, args);
        public object? InvokeWithArgs(object target, string name, object?[] args)
        {
            Calls.Add(name);
            if (name == "FeatureRevolve2")
            {
                RevolveArguments = args;
                _volume = scenario == "no_volume" ? 0 : 0.000183d;
                return scenario == "null_revolve" ? null : _feature;
            }
            return name switch
            {
                "CreateLine" => scenario == "null_line" ? null : new object(),
                "CreateCenterLine" => scenario == "null_axis" ? null : _axis,
                "GetActiveSketch2" => _sketch,
                "GetFeature" => null,
                "FeatureByPositionReverse" => args[0] is 0 && scenario != "null_profile" ? _profile : null,
                "GetSpecificFeature2" => scenario == "null_specific_sketch" ? null : scenario == "different_specific_sketch" ? new object() : _sketch,
                "CreateSelectData" => _selectData,
                "GetSelectedObjectCount2" => (int)args[0]! switch
                {
                    16 => _axisSelected ? 1 : 0,
                    -1 => scenario == "extra_selection" && _axisSelected ? 3 : _axisSelected ? 2 : 1,
                    _ => 1
                },
                "GetSelectedObject6" => _profile,
                "GetTypeName2" => ReferenceEquals(target, _profile)
                    ? scenario == "wrong_profile_type" ? "RefPlane" : "ProfileFeature"
                    : scenario == "wrong_feature_type" ? "Boss" : "Revolution",
                "GetDefinition" => _definition,
                "GetRevolutionAngle" => scenario == "wrong_angle" ? Math.PI : 2d * Math.PI,
                "IsBossFeature" => scenario != "cut_result",
                "IsThinFeature" => scenario == "thin_result",
                "GetBodies2" => _volume <= 0 ? null : scenario == "two_bodies" ? new[] { _body, new object() } : new[] { _body },
                "GetMassProperties" => new[] { 0d, 0d, 0d, _volume },
                "GetErrorCode" => 0,
                _ => null
            };
        }
        public object? TryInvoke(object? target, string name, params object?[] args) => target is null ? null : InvokeWithArgs(target, name, args);
        public object? TryInvokeWithArgs(object? target, string name, object?[] args)
        {
            if (name == "GetErrorCode2") { args[0] = false; return 0; }
            return TryInvoke(target, name, args);
        }
        public bool TryInvokeBool(object? target, string name, params object?[] args)
        {
            Calls.Add(name);
            if (name == "ForceRebuild3") return scenario != "rebuild_failed";
            if (name == "Select2")
            {
                ProfileSelections++;
                return ReferenceEquals(target, _profile) && args[0] is false && args[1] is 0;
            }
            if (name == "Select4")
            {
                AxisSelections++;
                _axisSelected = ReferenceEquals(target, _axis) && args[0] is true && _mark == 16;
                return _axisSelected;
            }
            return name == "SelectByID2";
        }
        public bool TrySetProperty(object target, string name, object? value)
        {
            if (name != "Mark") return false;
            _mark = (int)value!;
            return true;
        }
        public bool TryExtensionSaveAs(object model, string path, object? exportData, List<string> errors, List<string> warnings) => false;
        public void ReleaseComObject(object value) { }
    }
}
