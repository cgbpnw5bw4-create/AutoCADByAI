using AgentContracts;
using DomainSchemas;
using PlatformCore;
using PlatformCore.Modules.CADModeling.Reviewers;
using PlatformCore.Modules.CADModeling.Skills;
using SkillContracts;
using SolidWorksWorker;
using System.Globalization;
using System.Text.Json;
using WorkerContracts;

namespace PlatformSelfCheck.Tests;

public sealed class V18PartFamilyTests
{
    [Fact]
    public async Task GenericCadModelSpecFieldsFlowIntoBuildPlan()
    {
        var spec = new CADModelSpec(
            "flange-spec",
            FlangeBasicDefinition.Type,
            FlangeDimensions(),
            new Dictionary<string, string> { ["feature_finish"] = "machined" },
            material: "45 steel",
            outputRequirements: ["SLDPRT", "STEP"],
            drawingRequirements: new Dictionary<string, string> { ["drawing_scale"] = "1:1" },
            executionOptions: new Dictionary<string, string> { ["dry_run"] = "true" });

        var output = await new SolidWorksBuildPlanSkill().ExecuteAsync(new SkillInput(
            "generic-spec",
            nameof(CADModelSpec),
            spec,
            new Dictionary<string, string>()));

        var plan = Assert.IsType<SolidWorksBuildPlan>(output.Result);
        Assert.Equal("160", plan.Dimensions!["outer_diameter_mm"]);
        Assert.Equal("45 steel", plan.Material);
        Assert.Equal("machined", plan.Features!["feature_finish"]);
        Assert.Contains("STEP", plan.OutputRequirements!);
        Assert.Equal("1:1", plan.DrawingRequirements!["drawing_scale"]);
        Assert.Equal("true", plan.ExecutionOptions!["dry_run"]);
    }

    [Fact]
    public void GenericCadModelSpecRoundTripsStructuredJson()
    {
        var original = new CADModelSpec(
            "shaft-json-spec",
            ShaftBasicDefinition.Type,
            new Dictionary<string, string>
            {
                ["diameter_mm"] = "40",
                ["length_mm"] = "200"
            },
            new Dictionary<string, string> { ["feature_finish"] = "ground" },
            material: "40Cr",
            outputRequirements: ["SLDPRT", "STEP"],
            drawingRequirements: new Dictionary<string, string> { ["drawing_scale"] = "1:2" },
            executionOptions: new Dictionary<string, string> { ["dry_run"] = "true" });

        var json = JsonSerializer.Serialize(original);
        var roundTripped = Assert.IsType<CADModelSpec>(JsonSerializer.Deserialize<CADModelSpec>(json));

        Assert.Equal(ShaftBasicDefinition.Type, roundTripped.PartType);
        Assert.Equal("40", roundTripped.Dimensions["diameter_mm"]);
        Assert.Equal("ground", roundTripped.FeatureOptions["feature_finish"]);
        Assert.Equal("40Cr", roundTripped.Material);
        Assert.Contains("STEP", roundTripped.OutputRequirements);
        Assert.Equal("1:2", roundTripped.DrawingRequirements["drawing_scale"]);
        Assert.Equal("true", roundTripped.ExecutionOptions["dry_run"]);
    }

    [Fact]
    public void DefaultRegistryContainsFourIndependentSchemasAndRejectsDuplicates()
    {
        var registry = PartTypeRegistry.CreateDefault();

        var plate = Assert.IsType<PlateBasic4HolesDefinition>(registry.GetDefinition(PlateBasic4HolesDefinition.Type));
        var flange = Assert.IsType<FlangeBasicDefinition>(registry.GetDefinition(FlangeBasicDefinition.Type));
        var shaft = Assert.IsType<ShaftBasicDefinition>(registry.GetDefinition(ShaftBasicDefinition.Type));
        var jacket = Assert.IsType<JacketBasicDefinition>(registry.GetDefinition(JacketBasicDefinition.Type));
        Assert.Equal(5, plate.ParameterSchema.Count);
        Assert.Equal(6, flange.ParameterSchema.Count);
        Assert.Equal(4, shaft.ParameterSchema.Count);
        Assert.Equal(3, jacket.ParameterSchema.Count);
        Assert.NotSame(plate.Validator, flange.Validator);
        Assert.NotSame(flange.Validator, shaft.Validator);
        Assert.NotSame(shaft.Validator, jacket.Validator);
        Assert.Throws<InvalidOperationException>(() => registry.Register(new PlateBasic4HolesDefinition()));
    }

    [Fact]
    public void FlangeWithOneBoltHoleIsValidWhenRadialClearanceIsValid()
    {
        var dimensions = new Dictionary<string, string>(FlangeDimensions(), StringComparer.OrdinalIgnoreCase)
        {
            ["bolt_hole_count"] = "1"
        };

        var result = new FlangeBasicValidator().Validate(
            new CADModelSpec("single-bolt-flange", FlangeBasicDefinition.Type, dimensions));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues));
    }

    [Theory]
    [InlineData(PlateBasic4HolesDefinition.Type)]
    [InlineData(FlangeBasicDefinition.Type)]
    [InlineData(ShaftBasicDefinition.Type)]
    [InlineData(JacketBasicDefinition.Type)]
    public async Task RegisteredPartFamiliesGenerateReviewedPlansAndDryRunArtifacts(string partType)
    {
        var outputRoot = TempOutput(partType);
        try
        {
            var spec = Spec(partType);
            var skillOutput = await new SolidWorksBuildPlanSkill().ExecuteAsync(new SkillInput(
                $"plan-{partType}", nameof(CADModelSpec), spec, new Dictionary<string, string>()));
            var plan = Assert.IsType<SolidWorksBuildPlan>(skillOutput.Result);
            var review = new SolidWorksBuildPlanReviewer().Review(plan);
            var result = await new FakeSolidWorksWorker().ExecuteAsync(
                new SolidWorksWorkerRequest($"request-{partType}", plan, outputRoot),
                CancellationToken.None);

            Assert.Equal(SkillOutputStatus.Completed, skillOutput.Status);
            Assert.Equal(partType, plan.PartType);
            Assert.True(review.IsPassed, string.Join(Environment.NewLine, review.Issues));
            Assert.Equal("Completed", result.Status);
            Assert.Equal("Fake", result.ExecutionMode);
            Assert.False(result.RealCadExecuted);
            Assert.Null(result.FailureStage);
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith($"fake_{partType}.SLDPRT.txt", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.GeneratedArtifacts, artifact => artifact.FilePath.EndsWith($"fake_{partType}.STEP.txt", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Logs, log => log.Contains("part_family_builder", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteOutput(outputRoot);
        }
    }

    [Fact]
    public async Task ExplicitUnknownPartTypeEntersWorkflowAndReturnsPreciseFailureStage()
    {
        var root = FindProjectRoot();
        var outputRoot = TempOutput("unknown");
        try
        {
            var platform = PlatformBootstrapper.CreateDefault(root);
            var router = new SolidWorksWorkflowRouter();
            var context = Context(root, outputRoot, "unknown_family", new Dictionary<string, string>());
            var request = router.TryBuildRequest(context);

            Assert.NotNull(request);
            var result = await new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>()))
                .ExecuteAsync(request!);

            Assert.Equal(PartFamilyFailureStages.UnsupportedPartType, result.FailureStage);
            Assert.False(result.RealCadExecuted);
            Assert.Contains(result.Issues, issue => issue.StartsWith("unsupported_part_type:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteOutput(outputRoot);
        }
    }

    [Fact]
    public async Task SharedUnknownCadModelSpecReturnsUnsupportedBeforeWorker()
    {
        var root = FindProjectRoot();
        var outputRoot = TempOutput("shared-unknown");
        var platform = PlatformBootstrapper.CreateDefault(root);
        var worker = new CountingSolidWorksWorker();
        platform.WorkerRegistry.Register(worker);
        try
        {
            var router = new SolidWorksWorkflowRouter();
            var context = new AgentContext(
                $"task-{Guid.NewGuid():N}",
                new AgentInput(
                    "test",
                    "test",
                    $"conversation-{Guid.NewGuid():N}",
                    "user",
                    "Structured CAD request.",
                    [],
                    new Dictionary<string, string>
                    {
                        ["project_root"] = root,
                        ["solidworks_output_directory"] = outputRoot
                    }),
                new Dictionary<string, object?>
                {
                    ["cad_model_spec"] = new CADModelSpec(
                        "shared-unknown-spec",
                        "shared_unknown_family",
                        new Dictionary<string, string>())
                },
                DateTimeOffset.UtcNow);

            var request = router.TryBuildRequest(context);
            Assert.NotNull(request);
            var result = await new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>()))
                .ExecuteAsync(request!);

            Assert.Equal(PartFamilyFailureStages.UnsupportedPartType, result.FailureStage);
            Assert.Equal(0, worker.InvocationCount);
        }
        finally
        {
            DeleteOutput(outputRoot);
        }
    }

    [Fact]
    public async Task MissingAndInvalidParametersAreRejectedBeforeWorker()
    {
        var root = FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(root);
        var worker = new CountingSolidWorksWorker();
        platform.WorkerRegistry.Register(worker);
        var outputRoot = TempOutput("preworker-validation");
        try
        {
            var runner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>()));
            var missing = await runner.ExecuteAsync(Request(root, Path.Combine(outputRoot, "missing"), new CADModelSpec(
                "missing-flange", FlangeBasicDefinition.Type,
                new Dictionary<string, string> { ["outer_diameter_mm"] = "120" })));
            var invalid = await runner.ExecuteAsync(Request(root, Path.Combine(outputRoot, "invalid"), new CADModelSpec(
                "invalid-shaft", ShaftBasicDefinition.Type,
                new Dictionary<string, string> { ["diameter_mm"] = "-20", ["length_mm"] = "100" })));
            var invalidNanStep = await runner.ExecuteAsync(Request(root, Path.Combine(outputRoot, "invalid-nan-step"), new CADModelSpec(
                "invalid-shaft-nan-step", ShaftBasicDefinition.Type,
                new Dictionary<string, string>
                {
                    ["diameter_mm"] = "20", ["length_mm"] = "100",
                    ["optional_step_diameters"] = "NaN", ["optional_step_lengths"] = "10"
                })));
            var invalidInfiniteStep = await runner.ExecuteAsync(Request(root, Path.Combine(outputRoot, "invalid-infinite-step"), new CADModelSpec(
                "invalid-shaft-infinite-step", ShaftBasicDefinition.Type,
                new Dictionary<string, string>
                {
                    ["diameter_mm"] = "20", ["length_mm"] = "100",
                    ["optional_step_diameters"] = "30", ["optional_step_lengths"] = "Infinity"
                })));
            var overlappingFlangeHoles = await runner.ExecuteAsync(Request(root, Path.Combine(outputRoot, "overlapping-flange-holes"), new CADModelSpec(
                "invalid-flange-overlap", FlangeBasicDefinition.Type,
                new Dictionary<string, string>
                {
                    ["outer_diameter_mm"] = "160", ["inner_diameter_mm"] = "20", ["thickness_mm"] = "10",
                    ["bolt_hole_count"] = "100", ["bolt_hole_diameter_mm"] = "10", ["bolt_circle_diameter_mm"] = "100"
                })));
            var overlappingPlateHoles = await runner.ExecuteAsync(Request(root, Path.Combine(outputRoot, "overlapping-plate-holes"), new CADModelSpec(
                "invalid-plate-overlap", PlateBasic4HolesDefinition.Type,
                new Dictionary<string, string>
                {
                    ["length_mm"] = "42", ["width_mm"] = "42", ["thickness_mm"] = "10",
                    ["hole_diameter_mm"] = "38", ["hole_count"] = "4"
                })));

            Assert.Equal(PartFamilyFailureStages.MissingRequiredParameter, missing.FailureStage);
            Assert.Equal(PartFamilyFailureStages.InvalidParameterValue, invalid.FailureStage);
            Assert.Equal(PartFamilyFailureStages.InvalidParameterValue, invalidNanStep.FailureStage);
            Assert.Equal(PartFamilyFailureStages.InvalidParameterValue, invalidInfiniteStep.FailureStage);
            Assert.Equal(PartFamilyFailureStages.InvalidParameterValue, overlappingFlangeHoles.FailureStage);
            Assert.Equal(PartFamilyFailureStages.InvalidParameterValue, overlappingPlateHoles.FailureStage);
            Assert.Equal(0, worker.InvocationCount);
        }
        finally
        {
            DeleteOutput(outputRoot);
        }
    }

    [Fact]
    public async Task ShaftBuildPlanMapsCompleteHalfProfileAndOverallLength()
    {
        var output = await new SolidWorksBuildPlanSkill().ExecuteAsync(new SkillInput(
            "shaft-overall-length",
            nameof(CADModelSpec),
            Spec(ShaftBasicDefinition.Type),
            new Dictionary<string, string>()));

        var plan = Assert.IsType<SolidWorksBuildPlan>(output.Result);
        var profile = Assert.Single(plan.Operations, operation =>
            operation.OperationType.Equals("CreateSketch", StringComparison.OrdinalIgnoreCase));
        var segments = ShaftFeatureBuilder.BuildSegments(
            double.Parse(profile.Parameters["diameter_mm"], CultureInfo.InvariantCulture),
            double.Parse(profile.Parameters["length_mm"], CultureInfo.InvariantCulture),
            [32, 24],
            [40, 30]);
        var horizontalLength = segments
            .Where(segment => Math.Abs(segment.Y1 - segment.Y2) < 1e-12 && segment.Y1 > 0)
            .Sum(segment => segment.X2 - segment.X1);

        Assert.Equal("closed_half_section", profile.Parameters["profile"]);
        Assert.Equal(180d, horizontalLength, precision: 10);
        Assert.Contains(plan.Operations, operation => operation.OperationType == "CreateCenterLine");
        Assert.Contains(plan.Operations, operation => operation.OperationType == "RevolveBoss");
    }

    [Fact]
    public async Task RegistryDispatchesTemporaryFourthFamilyWithoutPartTypeSwitchChange()
    {
        var definitions = PartTypeRegistry.CreateDefault();
        definitions.Register(new TestFourthFamilyDefinition());
        var builders = PartFamilyBuilderRegistry.CreateDefault();
        builders.Register(new TestFourthFamilyBuilder());
        var outputRoot = TempOutput(TestFourthFamilyDefinition.Type);
        try
        {
            var spec = new CADModelSpec(
                "fourth-spec",
                TestFourthFamilyDefinition.Type,
                new Dictionary<string, string> { ["size_mm"] = "25" });
            var output = await new SolidWorksBuildPlanSkill(definitions).ExecuteAsync(new SkillInput(
                "fourth-family", nameof(CADModelSpec), spec, new Dictionary<string, string>()));
            var plan = Assert.IsType<SolidWorksBuildPlan>(output.Result);
            var result = await new FakeSolidWorksWorker(definitions, builders).ExecuteAsync(
                new SolidWorksWorkerRequest("fourth-request", plan, outputRoot));

            Assert.Equal("Completed", result.Status);
            Assert.Contains(result.Logs, log => log.Contains(nameof(TestFourthFamilyBuilder), StringComparison.Ordinal));
        }
        finally
        {
            DeleteOutput(outputRoot);
        }
    }

    [Fact]
    public async Task NonPlateRealRequestsWithMissingTemplateFailBeforeSessionConnection()
    {
        foreach (var partType in new[] { FlangeBasicDefinition.Type, ShaftBasicDefinition.Type, JacketBasicDefinition.Type })
        {
            var outputRoot = TempOutput($"real-{partType}");
            try
            {
                var planOutput = await new SolidWorksBuildPlanSkill().ExecuteAsync(new SkillInput(
                    $"real-{partType}", nameof(CADModelSpec), Spec(partType), new Dictionary<string, string>()));
                var plan = Assert.IsType<SolidWorksBuildPlan>(planOutput.Result);
                var session = new CountingSessionManager();
                var options = new SolidWorksRuntimeOptions(
                    EnableRealExecution: true,
                    Visible: false,
                    TemplatePartPath: Path.Combine(outputRoot, "unused-template.prtdot"),
                    OutputDirectory: outputRoot,
                    ConnectTimeoutSeconds: 1,
                    ExecutionTimeoutSeconds: 1,
                    MainWorkflowExecutionEnabled: true);
                var result = await new RealSolidWorksWorker(session, options).ExecuteAsync(new SolidWorksWorkerRequest(
                    $"real-{partType}", plan, outputRoot, DryRun: false, AllowRealCadExecution: true));

                if (partType == JacketBasicDefinition.Type)
                {
                    Assert.Equal("Rejected", result.Status);
                    Assert.Equal(PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient, result.FailureStage);
                }
                else
                {
                    Assert.Equal("Failed", result.Status);
                    Assert.Equal("preflight_failed", result.FailureStage);
                }

                Assert.False(result.RealCadExecuted);
                Assert.Equal(0, session.ConnectCount);
            }
            finally
            {
                DeleteOutput(outputRoot);
            }
        }
    }

    [Fact]
    public async Task CompletePackageDoesNotSilentlyConvertFlangeToPlate()
    {
        var root = FindProjectRoot();
        var outputRoot = TempOutput("flange-complete-package");
        try
        {
            var platform = PlatformBootstrapper.CreateDefault(root);
            var result = await new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => SolidWorksRuntimeOptions.FromEnvironment(new Dictionary<string, string?>()))
                .ExecuteAsync(new SolidWorksMainWorkflowRequest(
                    "flange-complete-package",
                    "flange-complete-package",
                    root,
                    outputRoot,
                    ModelSpec: Spec(FlangeBasicDefinition.Type),
                    Operation: SolidWorksMainWorkflowOperation.BuildCompleteDrawingPackage));

            Assert.Equal(PartFamilyFailureStages.PartFamilyBuilderMissing, result.FailureStage);
            Assert.False(result.RealCadExecuted);
            Assert.DoesNotContain(result.ArtifactPaths, path => path.EndsWith("plate_basic_4holes.SLDPRT", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteOutput(outputRoot);
        }
    }

    [Fact]
    public void DefaultDefinitionAndBuilderRegistriesExposeMatchingFamilyKeys()
    {
        var definitions = PartTypeRegistry.CreateDefault().GetAll()
            .Select(definition => definition.PartType)
            .OrderBy(partType => partType, StringComparer.OrdinalIgnoreCase);
        var builders = PartFamilyBuilderRegistry.CreateDefault().GetAll()
            .Select(builder => builder.PartType)
            .OrderBy(partType => partType, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(definitions, builders, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FakeWorkerRejectsRegisteredFamilyWhenBuilderIsMissing()
    {
        var outputRoot = TempOutput("missing-builder");
        try
        {
            var skillOutput = await new SolidWorksBuildPlanSkill().ExecuteAsync(new SkillInput(
                "missing-builder-plan",
                nameof(CADModelSpec),
                Spec(FlangeBasicDefinition.Type),
                new Dictionary<string, string>()));
            var plan = Assert.IsType<SolidWorksBuildPlan>(skillOutput.Result);
            var buildersWithoutFlange = new PartFamilyBuilderRegistry(
            [
                new PlateBasic4HolesPartFamilyBuilder(),
                new ShaftFeatureBuilder()
            ]);

            var result = await new FakeSolidWorksWorker(
                PartTypeRegistry.CreateDefault(),
                buildersWithoutFlange).ExecuteAsync(
                    new SolidWorksWorkerRequest("missing-builder", plan, outputRoot));

            Assert.Equal("Rejected", result.Status);
            Assert.Equal(PartFamilyFailureStages.PartFamilyBuilderMissing, result.FailureStage);
            Assert.Empty(result.GeneratedArtifacts);
            Assert.False(result.RealCadExecuted);
        }
        finally
        {
            DeleteOutput(outputRoot);
        }
    }

    [Fact]
    public async Task PartFamilyBuilderRejectsMismatchedPlanWithFamilyFailureStage()
    {
        var outputRoot = TempOutput("mismatched-builder");
        try
        {
            var skillOutput = await new SolidWorksBuildPlanSkill().ExecuteAsync(new SkillInput(
                "mismatched-builder-plan",
                nameof(CADModelSpec),
                Spec(ShaftBasicDefinition.Type),
                new Dictionary<string, string>()));
            var plan = Assert.IsType<SolidWorksBuildPlan>(skillOutput.Result);

            var result = await new FlangeFeatureBuilder().BuildDryRunAsync(
                new SolidWorksWorkerRequest("mismatched-builder", plan, outputRoot),
                Path.Combine(outputRoot, "artifacts"));

            Assert.Equal("Failed", result.Status);
            Assert.Equal(PartFamilyFailureStages.FlangeBuildFailed, result.FailureStage);
            Assert.Empty(result.Artifacts);
            Assert.False(Directory.Exists(outputRoot));
        }
        finally
        {
            DeleteOutput(outputRoot);
        }
    }

    private static CADModelSpec Spec(string partType) => partType switch
    {
        PlateBasic4HolesDefinition.Type => SolidWorksWorkflowRouter.CreatePlateBasicFourHolesSpec(),
        FlangeBasicDefinition.Type => new CADModelSpec("flange", partType, FlangeDimensions(), material: "Q235"),
        ShaftBasicDefinition.Type => new CADModelSpec("shaft", partType, new Dictionary<string, string>
        {
            ["diameter_mm"] = "40",
            ["length_mm"] = "180",
            ["optional_step_diameters"] = "32,24",
            ["optional_step_lengths"] = "40,30"
        }, material: "45 steel"),
        JacketBasicDefinition.Type => new CADModelSpec("jacket", partType, new Dictionary<string, string>
        {
            ["outer_diameter_mm"] = "140",
            ["inner_diameter_mm"] = "120",
            ["length_mm"] = "180"
        }, material: "Q235"),
        _ => throw new ArgumentOutOfRangeException(nameof(partType))
    };

    private static IReadOnlyDictionary<string, string> FlangeDimensions() => new Dictionary<string, string>
    {
        ["outer_diameter_mm"] = "160",
        ["inner_diameter_mm"] = "60",
        ["thickness_mm"] = "18",
        ["bolt_hole_count"] = "6",
        ["bolt_hole_diameter_mm"] = "14",
        ["bolt_circle_diameter_mm"] = "115"
    };

    private static SolidWorksMainWorkflowRequest Request(string root, string output, CADModelSpec spec) =>
        new($"request-{Guid.NewGuid():N}", $"task-{Guid.NewGuid():N}", root, output, ModelSpec: spec);

    private static AgentContext Context(
        string root,
        string output,
        string partType,
        IReadOnlyDictionary<string, string> additions)
    {
        var values = new Dictionary<string, string>(additions, StringComparer.OrdinalIgnoreCase)
        {
            ["project_root"] = root,
            ["solidworks_output_directory"] = output,
            ["part_type"] = partType
        };
        return new AgentContext(
            $"task-{Guid.NewGuid():N}",
            new AgentInput("test", "test", $"conversation-{Guid.NewGuid():N}", "user", "Structured CAD request.", [], values),
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);
    }

    private static string TempOutput(string name) =>
        Path.Combine(FindProjectRoot(), "output", "solidworks", $"v18-{name}-{Guid.NewGuid():N}");

    private static void DeleteOutput(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

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

    private sealed class CountingSolidWorksWorker : ISolidWorksWorker
    {
        public int InvocationCount { get; private set; }
        public string Name => nameof(FakeSolidWorksWorker);
        public string TargetSystem => "SolidWorks";

        public Task<SolidWorksWorkerResult> ExecuteAsync(SolidWorksWorkerRequest request, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            throw new InvalidOperationException("Worker must not be invoked for invalid CADModelSpec.");
        }

        public Task<WorkerOutput> ExecuteAsync(WorkerInput input) => ExecuteAsync(input, CancellationToken.None);

        public Task<WorkerOutput> ExecuteAsync(WorkerInput input, CancellationToken cancellationToken)
        {
            InvocationCount++;
            throw new InvalidOperationException("Worker must not be invoked for invalid CADModelSpec.");
        }
    }

    private sealed class CountingSessionManager : ISolidWorksSessionManager
    {
        public int ConnectCount { get; private set; }

        public Task<SolidWorksSessionConnectionResult> ConnectAsync(SolidWorksRuntimeOptions options, CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            throw new InvalidOperationException("SolidWorks connection must not be attempted for dry-run-only families.");
        }

        public Task<T> ExecuteWithApplicationAsync<T>(Func<object, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default, int? executionTimeoutSeconds = null) =>
            throw new InvalidOperationException("SolidWorks application must not be requested.");

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestFourthFamilyDefinition : IPartFamilyDefinition
    {
        public const string Type = "test_fourth_family";
        public string PartType => Type;
        public IReadOnlyList<PartParameterSchema> ParameterSchema { get; } =
            [new("size_mm", PartParameterValueKind.Number, true, "Test size.")];
        public IPartFamilyValidator Validator { get; } = new TestFourthFamilyValidator();
        public string BuilderFailureStage => "test_fourth_build_failed";
        public string ApiEvidence => "test_dry_run_evidence";

        public PartFamilyBuildPlanResult GenerateBuildPlan(string taskId, CADModelSpec spec)
        {
            var validation = Validator.Validate(spec);
            if (!validation.IsValid)
            {
                return new(null, validation.FailureStage, validation.Issues);
            }

            var plan = new SolidWorksBuildPlan(
                $"plan-{taskId}", spec.Id, "SolidWorks", Type, "mm",
                [
                    new SolidWorksOperation("op-001", "CreateSketch", "TopPlane", new Dictionary<string, string> { ["size_mm"] = spec.Dimensions["size_mm"] }, [], "Test profile."),
                    new SolidWorksOperation("op-002", "ExtrudeBoss", "TopPlane", new Dictionary<string, string> { ["depth_mm"] = spec.Dimensions["size_mm"] }, ["op-001"], "Test body."),
                    new SolidWorksOperation("op-003", "SavePart", "", new Dictionary<string, string>(), ["op-002"], "Save."),
                    new SolidWorksOperation("op-004", "ExportStep", "", new Dictionary<string, string>(), ["op-003"], "Export.")
                ],
                [
                    new SolidWorksArtifact("part", "Part", "fake_test_fourth_family.SLDPRT.txt", ".SLDPRT", false, 0, "Test part."),
                    new SolidWorksArtifact("step", "Step", "fake_test_fourth_family.STEP.txt", ".STEP", false, 0, "Test STEP."),
                    new SolidWorksArtifact("report", "BuildReport", "build_report.json", ".json", false, 0, "Test report.")
                ], [], []);
            return new(plan, null, []);
        }

        public IReadOnlyList<string> ReviewBuildPlan(SolidWorksBuildPlan plan) => [];
    }

    private sealed class TestFourthFamilyValidator : IPartFamilyValidator
    {
        public PartFamilyValidationResult Validate(CADModelSpec spec) =>
            spec.TryGetParameter("size_mm", out var value) && double.TryParse(value, out var parsed) && parsed > 0
                ? PartFamilyValidationResult.Passed()
                : PartFamilyValidationResult.Failed(PartFamilyFailureStages.InvalidParameterValue, "invalid_parameter_value: size_mm.");
    }

    private sealed class TestFourthFamilyBuilder : TextPlaceholderPartFamilyBuilder
    {
        public override string PartType => TestFourthFamilyDefinition.Type;
        public override string FailureStage => "test_fourth_build_failed";
        public override bool SupportsRealExecution => false;
        public override string ApiEvidence => "test_dry_run_evidence";
    }
}
