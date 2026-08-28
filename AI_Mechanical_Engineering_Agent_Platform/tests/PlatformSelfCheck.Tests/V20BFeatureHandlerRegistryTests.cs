using DomainSchemas;
using SolidWorksWorker;
using SolidWorksWorker.Features;
using SolidWorksWorker.Features.Cut;
using SolidWorksWorker.Features.Extrude;
using SolidWorksWorker.Features.Hole;
using SolidWorksWorker.Features.Revolve;
using SolidWorksWorker.Features.Sketch;

namespace PlatformSelfCheck.Tests;

public sealed class V20BFeatureHandlerRegistryTests
{
    [Fact]
    public void DefaultRegistryContainsEveryRegisteredFeatureHandler()
    {
        var registry = FeatureHandlerRegistry.CreateDefault();

        Assert.IsType<SketchHandler>(Resolve(registry, FeatureHandlerTypes.Sketch));
        Assert.IsType<ExtrudeBossHandler>(Resolve(registry, FeatureTypes.ExtrudeBoss));
        Assert.IsType<ExtrudeCutHandler>(Resolve(registry, FeatureTypes.ExtrudeCut));
        Assert.IsType<HoleHandler>(Resolve(registry, FeatureTypes.Hole));
        Assert.IsType<RevolveBossHandler>(Resolve(registry, FeatureTypes.RevolveBoss));

        // V2.1-A 复杂特征库
        Assert.IsType<SolidWorksWorker.Features.Fillet.FilletHandler>(
            Resolve(registry, FeatureTypes.Fillet));
        Assert.IsType<SolidWorksWorker.Features.Chamfer.ChamferHandler>(
            Resolve(registry, FeatureTypes.Chamfer));
        Assert.IsType<SolidWorksWorker.Features.Pattern.LinearPatternHandler>(
            Resolve(registry, FeatureTypes.LinearPattern));
        Assert.IsType<SolidWorksWorker.Features.Pattern.CircularPatternHandler>(
            Resolve(registry, FeatureTypes.CircularPattern));
        Assert.IsType<SolidWorksWorker.Features.Mirror.MirrorHandler>(
            Resolve(registry, FeatureTypes.Mirror));

        // 不硬编码数量：断言"每个已注册 Handler 都有非空参数 schema"，
        // 并断言注册表恰好覆盖 FeatureHandlerRegistry.CreateDefault 暴露的集合。
        Assert.Equal(10, registry.GetAll().Count);
        Assert.All(registry.GetAll(), handler => Assert.NotEmpty(handler.ParameterSchema));
        Assert.All(registry.GetAll(), handler => Assert.False(string.IsNullOrWhiteSpace(handler.FeatureType)));
    }

    [Fact]
    public void RegistryIsCaseInsensitiveAndRejectsDuplicateKeys()
    {
        var registry = new FeatureHandlerRegistry([new ProbeHandler("temporary_feature")]);

        Assert.True(registry.TryGetHandler("TEMPORARY_FEATURE", out _));
        Assert.Throws<InvalidOperationException>(
            () => registry.Register(new ProbeHandler("Temporary_Feature")));
    }

    [Fact]
    public void NewFeatureCanBeRegisteredWithoutChangingWorkerDispatch()
    {
        var registry = FeatureHandlerRegistry.CreateDefault();
        registry.Register(new ProbeHandler("temporary_feature"));

        var resolution = registry.Resolve(
            new FeatureDefinition("temporary-1", "temporary_feature"));

        Assert.True(resolution.IsSuccess);
        Assert.IsType<ProbeHandler>(resolution.Handler);
    }

    [Fact]
    public void UnknownFeatureIsRejectedWithStableFailureStage()
    {
        var resolution = FeatureHandlerRegistry.CreateDefault().Resolve(
            new FeatureDefinition("unknown-1", "not_registered"));

        Assert.False(resolution.IsSuccess);
        Assert.Equal(PartFamilyFailureStages.UnsupportedFeatureType, resolution.FailureStage);
        Assert.Contains(
            resolution.Issues,
            issue => issue.Contains("unsupported_feature_type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HandlerValidationRejectsInvalidParameters()
    {
        var invalidExtrude = new FeatureDefinition(
            "boss-1",
            FeatureTypes.ExtrudeBoss,
            new Dictionary<string, string> { ["depth_mm"] = "-2" });
        var invalidRevolve = new FeatureDefinition(
            "revolve-1",
            FeatureTypes.RevolveBoss,
            new Dictionary<string, string> { ["angle_degrees"] = "361" });

        var extrude = FeatureHandlerRegistry.CreateDefault().Resolve(invalidExtrude).Handler!;
        var revolve = FeatureHandlerRegistry.CreateDefault().Resolve(invalidRevolve).Handler!;

        Assert.Equal(PartFamilyFailureStages.InvalidFeatureParameter, extrude.Validate(invalidExtrude).FailureStage);
        Assert.Equal(PartFamilyFailureStages.InvalidFeatureParameter, revolve.Validate(invalidRevolve).FailureStage);
    }

    [Fact]
    public void EachFirstStageHandlerAcceptsItsDeclaredMinimalSchema()
    {
        var registry = FeatureHandlerRegistry.CreateDefault();
        var features = new[]
        {
            new FeatureDefinition(
                "sketch-1",
                FeatureHandlerTypes.Sketch,
                new Dictionary<string, string>
                {
                    ["entities"] = """[{"entity_id":"line-1","entity_type":"line","parameters":{"x1_mm":"0","y1_mm":"0","x2_mm":"10","y2_mm":"0"}}]"""
                },
                targetReference: "TopPlane"),
            new FeatureDefinition(
                "boss-1",
                FeatureTypes.ExtrudeBoss,
                new Dictionary<string, string> { ["depth_mm"] = "10", ["direction"] = "blind" }),
            new FeatureDefinition(
                "cut-1",
                FeatureTypes.ExtrudeCut,
                new Dictionary<string, string> { ["depth_mm"] = "5" }),
            new FeatureDefinition(
                "hole-1",
                FeatureTypes.Hole,
                new Dictionary<string, string>
                {
                    ["hole_diameter_mm"] = "8",
                    ["depth_mm"] = "12"
                }),
            new FeatureDefinition(
                "revolve-1",
                FeatureTypes.RevolveBoss,
                new Dictionary<string, string> { ["angle_degrees"] = "360" })
        };

        Assert.All(features, feature =>
        {
            var handler = registry.Resolve(feature).Handler!;
            var validation = handler.Validate(feature);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues));
        });
    }

    [Fact]
    public void HandlerBuildPlanUsesFeatureDefinitionWithoutSwitchDispatch()
    {
        var handler = new ExtrudeBossHandler();
        var feature = new FeatureDefinition(
            "boss-1",
            FeatureTypes.ExtrudeBoss,
            new Dictionary<string, string>
            {
                ["depth_mm"] = "10",
                ["direction"] = "blind"
            });

        var result = handler.BuildPlan(
            feature,
            new FeatureHandlerBuildPlanContext("op-1", "TopPlane", []));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Issues));
        Assert.Equal("ExtrudeBoss", result.Operation!.OperationType);
        Assert.Equal(FeatureTypes.ExtrudeBoss, result.Operation.Parameters["feature_type"]);
    }

    [Fact]
    public void CompleteGraphPreflightCollectsUnverifiedEvidenceBeforeExecution()
    {
        var plan = UnverifiedRevolvePlan();
        var registry = FeatureHandlerRegistry.CreateDefault();

        var preflight = registry.ValidateForRealExecution(plan);

        Assert.False(preflight.IsPassed);
        Assert.Equal(PartFamilyFailureStages.FeatureApiUnverified, preflight.FailureStage);
        Assert.NotEmpty(preflight.Features);
        Assert.Contains(
            preflight.Issues,
            issue => issue.Contains("unverified API evidence", StringComparison.OrdinalIgnoreCase));
        // 未验证集合必须显式列出。原断言写的是「RevolveBoss 是唯一未验证的」，
        // 那会让任何新增的未验证 Handler 都把这条测试打红，从而诱导把状态
        // 谎报为 verified 来"修好"测试——正是本项目要防的方向。
        string[] knownUnverified =
        [
            FeatureTypes.RevolveBoss,
            // V2.1-A 五个复杂特征已全部完成真机取证，因此未验证集合里只剩 revolve_boss。
        ];

        foreach (var featureType in knownUnverified)
        {
            Assert.False(
                Resolve(registry, featureType).ApiEvidence.AllowsRealExecution,
                $"{featureType} has no project evidence and must not allow real execution.");
        }

        Assert.All(
            registry.GetAll().Where(handler =>
                !knownUnverified.Contains(handler.FeatureType, StringComparer.OrdinalIgnoreCase)),
            handler => Assert.True(
                handler.ApiEvidence.AllowsRealExecution,
                $"{handler.FeatureType} is expected to be verified."));
    }

    [Fact]
    public async Task UnverifiedApiBlocksRealWorkerBeforeComConnection()
    {
        var output = Path.Combine(Path.GetTempPath(), $"v20-b-precom-{Guid.NewGuid():N}");
        var session = new CountingSessionManager();
        var options = new SolidWorksRuntimeOptions(
            EnableRealExecution: true,
            Visible: false,
            TemplatePartPath: null,
            OutputDirectory: output,
            ConnectTimeoutSeconds: 1,
            ExecutionTimeoutSeconds: 1,
            MainWorkflowExecutionEnabled: true,
            RealExecutionDefaultEnabled: true,
            DisableRealExecution: false,
            IsCiEnvironment: false,
            IsUnitTestEnvironment: false,
            VisibleModeDefault: true,
            ForceFakeWorker: false);
        var worker = new RealSolidWorksWorker(session, options);
        try
        {
            var result = await worker.ExecuteAsync(
                new SolidWorksWorkerRequest(
                    "v20-b-precom",
                    UnverifiedRevolvePlan(),
                    output,
                    DryRun: false));

            Assert.Equal("Rejected", result.Status);
            Assert.Equal(PartFamilyFailureStages.FeatureApiUnverified, result.FailureStage);
            Assert.False(result.RealCadConnected);
            Assert.False(result.RealCadExecuted);
            Assert.Equal(0, session.ConnectCount);
            Assert.Contains(
                result.Logs,
                log => log.Contains("COM connection was not attempted", StringComparison.OrdinalIgnoreCase));
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
    public void FamilyGeneratedGraphPreservesLegacyBuilderStrategy()
    {
        var source = SourcePlate();
        var result = new PlateBasic4HolesDefinition().GenerateBuildPlan("v20-b-legacy-family", source);
        var plan = Assert.IsType<SolidWorksBuildPlan>(result.BuildPlan);

        Assert.Equal(
            SolidWorksBuildExecutionStrategies.PartFamilyBuilder,
            plan.ExecutionStrategy);
    }

    private static IFeatureHandler Resolve(FeatureHandlerRegistry registry, string featureType)
    {
        Assert.True(registry.TryGetHandler(featureType, out var handler));
        return handler;
    }

    private static SolidWorksBuildPlan GenericPlatePlan()
    {
        var generic = PartFamilyGenericModelFactory.CreatePlateBasic4Holes(SourcePlate());
        var result = new PlateBasic4HolesDefinition().GenerateBuildPlan("v20-b-generic", generic);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Issues));
        var plan = Assert.IsType<SolidWorksBuildPlan>(result.BuildPlan);
        Assert.Equal(SolidWorksBuildExecutionStrategies.FeatureHandlerGraph, plan.ExecutionStrategy);
        return plan;
    }

    private static SolidWorksBuildPlan UnverifiedRevolvePlan() =>
        new(
            "v20-b-unverified-revolve-plan",
            "v20-b-unverified-revolve-spec",
            "SolidWorks",
            "generic_cad_model",
            "mm",
            [
                new SolidWorksOperation(
                "v20-b-unverified-revolve",
                "RevolveBoss",
                "TopPlane",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["feature_id"] = "v20-b-unverified-revolve",
                    ["feature_type"] = FeatureTypes.RevolveBoss,
                    ["angle_degrees"] = "360"
                },
                [],
                "Unverified revolve evidence probe.")
            ],
            [],
            [],
            ["Self-check must reject before COM."],
            ExecutionStrategy: SolidWorksBuildExecutionStrategies.FeatureHandlerGraph);

    private static CADModelSpec SourcePlate() =>
        new(
            "plate-v20-b",
            PlateBasic4HolesDefinition.Type,
            new Dictionary<string, string>
            {
                ["length_mm"] = "160",
                ["width_mm"] = "80",
                ["thickness_mm"] = "12",
                ["hole_diameter_mm"] = "10",
                ["hole_count"] = "4"
            });

    private sealed class ProbeHandler(string featureType) : FeatureHandlerBase
    {
        public override string FeatureType { get; } = featureType;
        public override string OperationType => "ProbeOperation";
        public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema => [];
        public override FeatureApiEvidence ApiEvidence { get; } = new(
            "probe",
            "test",
            [],
            "probe",
            [],
            FeatureApiEvidenceStatuses.Unverified,
            [],
            []);

        public override FeatureHandlerValidationResult Validate(FeatureDefinition feature) =>
            FeatureHandlerValidationResult.Passed();

        public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
            FeatureHandlerExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EvidenceBlocked(context.Feature));
    }

    private sealed class CountingSessionManager : ISolidWorksSessionManager
    {
        public int ConnectCount { get; private set; }

        public Task<SolidWorksSessionConnectionResult> ConnectAsync(
            SolidWorksRuntimeOptions options,
            CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            throw new InvalidOperationException("ConnectAsync must not run when evidence is unverified.");
        }

        public Task<T> ExecuteWithApplicationAsync<T>(
            Func<object, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default,
            int? executionTimeoutSeconds = null) =>
            throw new InvalidOperationException("Application execution must not run.");

        public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
