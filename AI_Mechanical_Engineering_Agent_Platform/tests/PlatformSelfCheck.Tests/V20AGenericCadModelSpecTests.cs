using DomainSchemas;
using PlatformCore;
using PlatformCore.Modules.CADModeling.Skills;
using PlatformCore.Modules.CADModeling.Validators;
using SolidWorksWorker;
using System.Reflection;
using System.Text.Json;
using WorkerContracts;

namespace PlatformSelfCheck.Tests;

public sealed class V20AGenericCadModelSpecTests
{
    [Fact]
    public void CanonicalAndLegacyJsonNormalizeToOneModelSpec()
    {
        const string legacyJson = """
            {
              "id": "legacy-flange",
              "part_type": "flange_basic",
              "dimensions": {
                "outer_diameter_mm": "160",
                "inner_diameter_mm": "60",
                "thickness_mm": "18",
                "bolt_hole_count": "6",
                "bolt_hole_diameter_mm": "14",
                "bolt_circle_diameter_mm": "115"
              },
              "features": { "feature_finish": "machined" },
              "material": "Q235"
            }
            """;

        var legacy = Assert.IsType<CADModelSpec>(JsonSerializer.Deserialize<CADModelSpec>(legacyJson));
        var generic = PartFamilyGenericModelFactory.CreateFlangeBasic(legacy);
        var roundTripped = Assert.IsType<CADModelSpec>(
            JsonSerializer.Deserialize<CADModelSpec>(JsonSerializer.Serialize(generic)));

        Assert.Equal("legacy-flange", legacy.ModelId);
        Assert.Equal(legacy.ModelId, legacy.Id);
        Assert.Equal(FlangeBasicDefinition.Type, legacy.ModelType);
        Assert.Equal(legacy.ModelType, legacy.PartType);
        Assert.Equal("160", legacy.Parameters["outer_diameter_mm"]);
        Assert.Equal("machined", legacy.FeatureOptions["feature_finish"]);
        Assert.Equal(generic.ModelId, roundTripped.ModelId);
        Assert.Equal(generic.ModelType, roundTripped.ModelType);
        Assert.NotEmpty(roundTripped.Sketches);
        Assert.NotEmpty(roundTripped.Features);
    }

    [Fact]
    public void FirstSketchAndFeatureVocabularyIsRegistered()
    {
        Assert.All(
            new[] { "line", "rectangle", "circle", "arc", "slot" },
            item => Assert.Contains(item, SketchEntityTypes.Supported));
        Assert.All(
            new[]
            {
                "horizontal", "vertical", "coincident", "concentric", "tangent",
                "parallel", "perpendicular", "equal", "dimensional"
            },
            item => Assert.Contains(item, SketchConstraintTypes.Supported));
        Assert.All(
            new[]
            {
                "extrude_boss", "extrude_cut", "revolve_boss", "revolve_cut", "hole",
                "fillet", "chamfer", "linear_pattern", "circular_pattern", "mirror"
            },
            item => Assert.Contains(item, FeatureTypes.Supported));
    }

    [Fact]
    public void FeatureGraphProducesStableTopologicalOrder()
    {
        var graph = new FeatureGraph(
        [
            Feature("cut", FeatureTypes.ExtrudeCut, ["base"], order: 2),
            Feature("base", FeatureTypes.ExtrudeBoss, [], order: 1),
            Feature("fillet", FeatureTypes.Fillet, ["cut"], order: 3)
        ]);

        var first = graph.ValidateAndSort();
        var second = graph.ValidateAndSort();

        Assert.True(first.IsValid, string.Join(Environment.NewLine, first.Issues));
        Assert.Equal(["base", "cut", "fillet"], first.OrderedFeatures.Select(item => item.FeatureId));
        Assert.Equal(
            first.OrderedFeatures.Select(item => item.FeatureId),
            second.OrderedFeatures.Select(item => item.FeatureId));
    }

    [Fact]
    public void FeatureGraphRejectsMissingDependencyCycleAndInvalidOrder()
    {
        var missing = new FeatureGraph(
            [Feature("cut", FeatureTypes.ExtrudeCut, ["missing"], order: 1)])
            .ValidateAndSort();
        var cycle = new FeatureGraph(
        [
            Feature("a", FeatureTypes.ExtrudeBoss, ["b"]),
            Feature("b", FeatureTypes.ExtrudeCut, ["a"])
        ]).ValidateAndSort();
        var invalidOrder = new FeatureGraph(
        [
            Feature("base", FeatureTypes.ExtrudeBoss, [], order: 2),
            Feature("cut", FeatureTypes.ExtrudeCut, ["base"], order: 1)
        ]).ValidateAndSort();

        Assert.Equal(PartFamilyFailureStages.FeatureDependencyMissing, missing.FailureStage);
        Assert.Equal(PartFamilyFailureStages.FeatureDependencyCycle, cycle.FailureStage);
        Assert.Equal(PartFamilyFailureStages.InvalidFeatureOrder, invalidOrder.FailureStage);
    }

    [Theory]
    [InlineData(PlateBasic4HolesDefinition.Type)]
    [InlineData(FlangeBasicDefinition.Type)]
    [InlineData(ShaftBasicDefinition.Type)]
    [InlineData(JacketBasicDefinition.Type)]
    public void RegisteredPartFamiliesCompileFromGenericFeatureGraph(string partType)
    {
        var definition = PartTypeRegistry.CreateDefault().GetDefinition(partType);
        Assert.NotNull(definition);
        var result = definition!.GenerateBuildPlan($"v20-a-{partType}", SourceSpec(partType));
        var plan = Assert.IsType<SolidWorksBuildPlan>(result.BuildPlan);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Issues));
        Assert.Contains(plan.Operations, operation => operation.OperationType == "CreateSketch");
        Assert.Contains(plan.Operations, operation => operation.Parameters.ContainsKey("feature_id"));
        Assert.Contains(plan.Operations, operation => operation.Parameters.ContainsKey("sketch_id"));
        Assert.True(new SolidWorksBuildPlanValidator().Validate(plan).IsPassed);
        Assert.Equal("SavePart", plan.Operations[^2].OperationType);
        Assert.Equal("ExportStep", plan.Operations[^1].OperationType);
    }

    [Fact]
    public void CadModelSpecValidatorRejectsInvalidGenericDescription()
    {
        var valid = PartFamilyGenericModelFactory.CreateFlangeBasic(SourceSpec(FlangeBasicDefinition.Type));
        var invalidUnit = new CADModelSpecValidator().Validate(valid with { Unit = "parsec" });
        var missingSketch = new CADModelSpecValidator().Validate(valid with
        {
            Features =
            [
                valid.Features[0] with { ReferencedSketches = ["missing_sketch"] }
            ]
        });
        var invalidParameter = new CADModelSpecValidator().Validate(valid with
        {
            Features =
            [
                valid.Features[0] with
                {
                    Parameters = new Dictionary<string, string> { ["depth_mm"] = "-1" }
                }
            ]
        });

        Assert.Equal(PartFamilyFailureStages.InvalidCadModelSpec, invalidUnit.FailureStage);
        Assert.Equal(PartFamilyFailureStages.SketchReferenceMissing, missingSketch.FailureStage);
        Assert.Equal(PartFamilyFailureStages.InvalidFeatureParameter, invalidParameter.FailureStage);
    }

    [Fact]
    public void BuildPlanValidatorRejectsTamperedOperationCycle()
    {
        var generated = new PlateBasic4HolesDefinition().GenerateBuildPlan(
            "tampered-cycle",
            SourceSpec(PlateBasic4HolesDefinition.Type));
        var plan = Assert.IsType<SolidWorksBuildPlan>(generated.BuildPlan);
        var operations = plan.Operations.ToArray();
        operations[0] = operations[0] with { DependsOn = [operations[1].OperationId] };

        var report = new SolidWorksBuildPlanValidator().Validate(plan with { Operations = operations });

        Assert.False(report.IsPassed);
        Assert.True(report.HasFatalError);
        Assert.Contains(
            report.Issues,
            issue => issue.Contains(
                PartFamilyFailureStages.FeatureDependencyCycle,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CyclicFeatureGraphIsRejectedBeforeWorkerDispatch()
    {
        var root = FindProjectRoot();
        var output = Path.Combine(root, "output", "solidworks", $"v20-a-cycle-{Guid.NewGuid():N}");
        var platform = PlatformBootstrapper.CreateDefault(root);
        var worker = new CountingWorker();
        platform.WorkerRegistry.Register(worker);
        var source = PartFamilyGenericModelFactory.CreatePlateBasic4Holes(SourceSpec(PlateBasic4HolesDefinition.Type));
        var cyclic = source with
        {
            Features =
            [
                source.Features[0] with
                {
                    Dependencies = ["plate_hole_cut"],
                    ReferencedFeatures = ["plate_hole_cut"]
                },
                source.Features[1]
            ]
        };
        try
        {
            var result = await new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => SolidWorksRuntimeOptions.FromEnvironment(
                    new Dictionary<string, string?>(),
                    isUnitTestEnvironment: true))
                .ExecuteAsync(new SolidWorksMainWorkflowRequest(
                    "v20-a-cycle",
                    "v20-a-cycle",
                    root,
                    output,
                    DryRun: false,
                    ModelSpec: cyclic));

            Assert.Equal(PartFamilyFailureStages.FeatureDependencyCycle, result.FailureStage);
            Assert.Equal(0, worker.InvocationCount);
            Assert.False(result.RealCadExecuted);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public void ValidGenericFeatureGraphCarriesTrustedHandlerExecutionStrategy()
    {
        var generic = PartFamilyGenericModelFactory.CreatePlateBasic4Holes(
            SourceSpec(PlateBasic4HolesDefinition.Type));
        var result = new PlateBasic4HolesDefinition().GenerateBuildPlan(
            "v20-b-trusted-strategy",
            generic);

        var plan = Assert.IsType<SolidWorksBuildPlan>(result.BuildPlan);
        Assert.Equal(
            SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
            plan.ExecutionStrategy);
    }

    [Fact]
    public async Task ValidGenericFeatureGraphDryRunPassesMainWorkflowQualityGate()
    {
        var root = FindProjectRoot();
        var output = Path.Combine(root, "output", "solidworks", $"v20-a-dry-run-{Guid.NewGuid():N}");
        var platform = PlatformBootstrapper.CreateDefault(root);
        var generic = PartFamilyGenericModelFactory.CreatePlateBasic4Holes(
            SourceSpec(PlateBasic4HolesDefinition.Type));
        try
        {
            var result = await new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => SolidWorksRuntimeOptions.FromEnvironment(
                    new Dictionary<string, string?>(),
                    isUnitTestEnvironment: true))
                .ExecuteAsync(new SolidWorksMainWorkflowRequest(
                    "v20-a-dry-run",
                    "v20-a-dry-run",
                    root,
                    output,
                    DryRun: true,
                    ModelSpec: generic));

            Assert.Equal("Completed", result.Status);
            Assert.True(result.QualityGatePassed);
            Assert.Equal("Passed", result.QualityGateDecision);
            Assert.False(result.RealCadExecuted);
            Assert.False(result.RealCadConnected);
            Assert.Contains(
                result.WorkflowResult.Steps,
                step => step.StepId == "solidworks-artifact-quality-gate" &&
                        step.Status == WorkflowStepStatus.Passed);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public void InjectedSessionManagerCannotRemoveDefaultEnvironmentProbe()
    {
        var worker = new RealSolidWorksWorker(
            new NoOpSessionManager(),
            SolidWorksRuntimeOptions.FromEnvironment(
                new Dictionary<string, string?>(),
                isUnitTestEnvironment: true));
        var field = typeof(RealSolidWorksWorker).GetField(
            "_executionEnvironmentProbe",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.IsType<SolidWorksExecutionEnvironmentProbe>(field.GetValue(worker));
    }

    private static FeatureDefinition Feature(
        string id,
        string type,
        IReadOnlyList<string> dependencies,
        int? order = null) =>
        new(
            id,
            type,
            new Dictionary<string, string>(),
            dependencies,
            referencedSketches: [],
            referencedFeatures: dependencies,
            executionOrder: order);

    private static CADModelSpec SourceSpec(string partType) => partType switch
    {
        PlateBasic4HolesDefinition.Type => new CADModelSpec(
            "plate-v20-a",
            partType,
            new Dictionary<string, string>
            {
                ["length_mm"] = "160",
                ["width_mm"] = "80",
                ["thickness_mm"] = "12",
                ["hole_diameter_mm"] = "10",
                ["hole_count"] = "4"
            }),
        FlangeBasicDefinition.Type => new CADModelSpec(
            "flange-v20-a",
            partType,
            new Dictionary<string, string>
            {
                ["outer_diameter_mm"] = "160",
                ["inner_diameter_mm"] = "60",
                ["thickness_mm"] = "18",
                ["bolt_hole_count"] = "6",
                ["bolt_hole_diameter_mm"] = "14",
                ["bolt_circle_diameter_mm"] = "115"
            }),
        ShaftBasicDefinition.Type => new CADModelSpec(
            "shaft-v20-a",
            partType,
            new Dictionary<string, string>
            {
                ["diameter_mm"] = "40",
                ["length_mm"] = "180",
                ["optional_step_diameters"] = "32,24",
                ["optional_step_lengths"] = "40,30"
            }),
        JacketBasicDefinition.Type => new CADModelSpec(
            "jacket-v21-a",
            partType,
            new Dictionary<string, string>
            {
                ["outer_diameter_mm"] = "140",
                ["inner_diameter_mm"] = "120",
                ["length_mm"] = "180"
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(partType))
    };

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

        throw new DirectoryNotFoundException("Project root was not found.");
    }

    private sealed class CountingWorker : ISolidWorksWorker
    {
        public int InvocationCount { get; private set; }
        public string Name => nameof(FakeSolidWorksWorker);
        public string TargetSystem => "SolidWorks";

        public Task<SolidWorksWorkerResult> ExecuteAsync(
            SolidWorksWorkerRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            throw new InvalidOperationException("Worker must not run for an invalid FeatureGraph.");
        }

        public Task<WorkerOutput> ExecuteAsync(WorkerInput input) =>
            ExecuteAsync(input, CancellationToken.None);

        public Task<WorkerOutput> ExecuteAsync(
            WorkerInput input,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            throw new InvalidOperationException("Worker must not run for an invalid FeatureGraph.");
        }
    }

    private sealed class NoOpSessionManager : ISolidWorksSessionManager
    {
        public Task<SolidWorksSessionConnectionResult> ConnectAsync(
            SolidWorksRuntimeOptions options,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Connection is not expected.");

        public Task<T> ExecuteWithApplicationAsync<T>(
            Func<object, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default,
            int? executionTimeoutSeconds = null) =>
            throw new InvalidOperationException("Application access is not expected.");

        public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
