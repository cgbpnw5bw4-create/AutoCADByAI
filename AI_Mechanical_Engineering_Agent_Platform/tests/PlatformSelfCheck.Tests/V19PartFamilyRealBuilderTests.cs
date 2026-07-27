using System.Collections.Concurrent;
using System.Text.Json;
using DomainSchemas;
using PlatformCore.Modules.CADModeling.Reviewers;
using PlatformCore.Modules.CADModeling.Validators;
using SolidWorksPartFamilySmokeRunner;
using SolidWorksWorker;

namespace PlatformSelfCheck.Tests;

public sealed class V19PartFamilyRealBuilderTests
{
    [Fact]
    public void RequiredFailureStagesAreStable()
    {
        Assert.Equal("part_family_api_evidence_insufficient", PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient);
        Assert.Equal("flange_profile_create_failed", PartFamilyFailureStages.FlangeProfileCreateFailed);
        Assert.Equal("flange_extrude_failed", PartFamilyFailureStages.FlangeExtrudeFailed);
        Assert.Equal("flange_inner_cut_failed", PartFamilyFailureStages.FlangeInnerCutFailed);
        Assert.Equal("flange_bolt_holes_failed", PartFamilyFailureStages.FlangeBoltHolesFailed);
        Assert.Equal("shaft_profile_create_failed", PartFamilyFailureStages.ShaftProfileCreateFailed);
        Assert.Equal("shaft_revolve_failed", PartFamilyFailureStages.ShaftRevolveFailed);
        Assert.Equal("shaft_step_feature_failed", PartFamilyFailureStages.ShaftStepFeatureFailed);
        Assert.Equal("part_save_failed", PartFamilyFailureStages.PartSaveFailed);
        Assert.Equal("step_export_failed", PartFamilyFailureStages.StepExportFailed);
        Assert.Equal("artifact_validation_failed", PartFamilyFailureStages.ArtifactValidationFailed);
        Assert.Equal("quality_gate_rejected", PartFamilyFailureStages.QualityGateRejected);
    }

    [Fact]
    public void ShaftFeatureRevolve2UsesExactTwentyArgumentEvidenceContract()
    {
        var args = ShaftFeatureBuilder.FeatureRevolve2Arguments();

        Assert.Equal(20, args.Length);
        Assert.Equal(
            new object?[]
            {
                true, true, false, false, false, false, 0, 0,
                2d * Math.PI, 0d, false, false, 0.01d, 0.01d,
                0, 0, 0, true, true, true
            },
            args);
    }

    [Fact]
    public async Task SemanticReviewersValidateFlangeAndShaftSequencesAndMappings()
    {
        var flangePlan = Assert.IsType<SolidWorksBuildPlan>(new FlangeBasicDefinition()
            .GenerateBuildPlan("flange-review", FlangeSpec()).BuildPlan);
        var shaftPlan = Assert.IsType<SolidWorksBuildPlan>(new ShaftBasicDefinition()
            .GenerateBuildPlan("shaft-review", ShaftSpec()).BuildPlan);
        var reviewer = new SolidWorksBuildPlanReviewer();

        Assert.True(reviewer.Review(flangePlan).IsPassed);
        Assert.True(reviewer.Review(shaftPlan).IsPassed);

        var tamperedFlangeOperations = flangePlan.Operations.ToArray();
        tamperedFlangeOperations[3] = tamperedFlangeOperations[3] with
        {
            Parameters = new Dictionary<string, string>(tamperedFlangeOperations[3].Parameters)
            {
                ["hole_diameter_mm"] = "999"
            }
        };
        var tamperedShaftOperations = shaftPlan.Operations.ToArray();
        tamperedShaftOperations[2] = tamperedShaftOperations[2] with
        {
            Parameters = new Dictionary<string, string>(tamperedShaftOperations[2].Parameters)
            {
                ["axis_selection_mark"] = "0"
            }
        };

        Assert.False(reviewer.Review(flangePlan with { Operations = tamperedFlangeOperations }).IsPassed);
        Assert.False(reviewer.Review(shaftPlan with { Operations = tamperedShaftOperations }).IsPassed);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RealWorkerDispatchesThroughRegistryWithoutPartTypeBranch()
    {
        var root = TempRoot("dispatch");
        Directory.CreateDirectory(root);
        var template = Path.Combine(root, "part.prtdot");
        await File.WriteAllTextAsync(template, "template");
        try
        {
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.FlangeBasic);
            var registry = new PartFamilyBuilderRegistry([builder]);
            var session = new CoordinatedSessionState().CreateManager();
            var worker = Worker(session, root, template, registry);
            var result = await worker.ExecuteAsync(RealRequest(FlangePlan(), root));

            Assert.Equal("Completed", result.Status);
            Assert.Equal(1, builder.BuildCount);
            Assert.Equal(PartFamilyExecutionModes.FlangeBasic, result.ExecutionMode);
            Assert.True(result.RealCadExecuted);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessStaticCoordinatorSerializesDifferentWorkerInstances()
    {
        var root = TempRoot("coordination");
        Directory.CreateDirectory(root);
        var template = Path.Combine(root, "part.prtdot");
        await File.WriteAllTextAsync(template, "template");
        try
        {
            var state = new CoordinatedSessionState();
            var builderOne = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.FlangeBasic, 100);
            var builderTwo = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.FlangeBasic, 100);
            var workerOne = Worker(
                state.CreateManager(), Path.Combine(root, "one"), template, new PartFamilyBuilderRegistry([builderOne]));
            var workerTwo = Worker(
                state.CreateManager(), Path.Combine(root, "two"), template, new PartFamilyBuilderRegistry([builderTwo]));

            var results = await Task.WhenAll(
                workerOne.ExecuteAsync(RealRequest(FlangePlan(), Path.Combine(root, "one"))),
                workerTwo.ExecuteAsync(RealRequest(FlangePlan(), Path.Combine(root, "two"))));

            Assert.All(results, result => Assert.Equal("Completed", result.Status));
            Assert.Equal(1, state.MaxConcurrentSessions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerBuildsNonPlateWithoutLegacyLocalAuthorization()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai_me_v19_worker_auth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var template = Path.Combine(root, "part.prtdot");
        await File.WriteAllTextAsync(template, "template");
        try
        {
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.FlangeBasic);
            var sessionState = new CoordinatedSessionState();
            var worker = Worker(
                sessionState.CreateManager(),
                root,
                template,
                new PartFamilyBuilderRegistry([builder]));

            var result = await worker.ExecuteAsync(RealRequest(FlangePlan(), root));

            Assert.Equal("Completed", result.Status);
            Assert.Null(result.FailureStage);
            Assert.True(result.RealCadConnected);
            Assert.True(result.RealCadExecuted);
            Assert.Equal(1, sessionState.MaxConcurrentSessions);
            Assert.Equal(1, builder.BuildCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NonPlatePreflightFailureReportDoesNotClaimCadConnection()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai_me_v19_preflight_truth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var missingTemplate = Path.Combine(root, "missing.prtdot");
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.FlangeBasic);
            var sessionState = new CoordinatedSessionState();
            var worker = Worker(
                sessionState.CreateManager(),
                root,
                missingTemplate,
                new PartFamilyBuilderRegistry([builder]));

            var result = await worker.ExecuteAsync(RealRequest(FlangePlan(), root));

            Assert.Equal("Failed", result.Status);
            Assert.Equal("preflight_failed", result.FailureStage);
            Assert.False(result.RealCadConnected);
            Assert.False(result.RealCadExecuted);
            Assert.Equal(0, sessionState.MaxConcurrentSessions);
            AssertFailureReportTruth(result, "preflight_failed");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NonPlateConnectionFailureReportDoesNotClaimCadConnection()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai_me_v19_connection_truth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var template = Path.Combine(root, "part.prtdot");
        await File.WriteAllTextAsync(template, "template");
        try
        {
            var session = new FailingConnectionSessionManager();
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.FlangeBasic);
            var worker = Worker(session, root, template, new PartFamilyBuilderRegistry([builder]));

            var result = await worker.ExecuteAsync(RealRequest(FlangePlan(), root));

            Assert.Equal("Failed", result.Status);
            Assert.Equal("solidworks_connection_failed", result.FailureStage);
            Assert.False(result.RealCadConnected);
            Assert.False(result.RealCadExecuted);
            Assert.Equal(1, session.ConnectAttempts);
            Assert.Equal(0, builder.BuildCount);
            AssertFailureReportTruth(result, "solidworks_connection_failed");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultPartFamilySmokeToolNeverInvokesCad()
    {
        var root = TempRoot("smoke-default-off");
        var invocations = 0;
        try
        {
            var runner = new PartFamilyApiSmokeRunner((_, _, _) =>
            {
                Interlocked.Increment(ref invocations);
                throw new InvalidOperationException("CAD must remain disabled.");
            });
            var report = await runner.RunAsync(
                new PartFamilySmokeRunnerOptions(FlangeBasicDefinition.Type, root, Enabled: false, Visible: false),
                CancellationToken.None);

            Assert.Equal("Skipped", report.FinalStatus);
            Assert.Equal(0, invocations);
            Assert.False(report.SolidWorksConnected);
            Assert.True(File.Exists(report.EvidenceReportPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ArtifactValidatorUsesRegistryMetadataForFlangeRealBuild()
    {
        var root = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"v19-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var part = Write(root, "flange_basic.SLDPRT", "part");
            var step = Write(root, "flange_basic.STEP", "step");
            var reportPath = Path.Combine(root, "build_report.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                part_type = FlangeBasicDefinition.Type,
                execution_mode = PartFamilyExecutionModes.FlangeBasic,
                real_cad_executed = true,
                real_cad_connected = true,
                sldprt_save_success = true,
                step_export_success = true,
                final_status = "Passed"
            }));
            var result = new SolidWorksWorkerResult(
                "validator",
                "Completed",
                [Artifact(part, ".SLDPRT"), Artifact(step, ".STEP"), Artifact(reportPath, ".json")],
                [],
                [],
                PartFamilyExecutionModes.FlangeBasic,
                true,
                true);

            var validation = new SolidWorksArtifactValidator().Validate(result);

            Assert.True(validation.IsPassed, string.Join(Environment.NewLine, validation.Issues));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RealSolidWorksWorker Worker(
        ISolidWorksSessionManager session,
        string output,
        string template,
        PartFamilyBuilderRegistry registry) =>
        new(
            session,
            new SolidWorksRuntimeOptions(true, false, template, output, 2, 5, MainWorkflowExecutionEnabled: true),
            null,
            null,
            null,
            null,
            registry);

    private static SolidWorksWorkerRequest RealRequest(SolidWorksBuildPlan plan, string output) =>
        new($"request-{Guid.NewGuid():N}", plan, output, DryRun: false, AllowRealCadExecution: true);

    private static SolidWorksBuildPlan FlangePlan() =>
        new FlangeBasicDefinition().GenerateBuildPlan("test", FlangeSpec()).BuildPlan!;

    private static CADModelSpec FlangeSpec() => new(
        "flange",
        FlangeBasicDefinition.Type,
        new Dictionary<string, string>
        {
            ["outer_diameter_mm"] = "160",
            ["inner_diameter_mm"] = "60",
            ["thickness_mm"] = "18",
            ["bolt_hole_count"] = "6",
            ["bolt_hole_diameter_mm"] = "14",
            ["bolt_circle_diameter_mm"] = "115"
        });

    private static CADModelSpec ShaftSpec() => new(
        "shaft",
        ShaftBasicDefinition.Type,
        new Dictionary<string, string>
        {
            ["diameter_mm"] = "40",
            ["length_mm"] = "180",
            ["optional_step_diameters"] = "32,24",
            ["optional_step_lengths"] = "40,30"
        });

    private static SolidWorksArtifact Artifact(string path, string extension)
    {
        var info = new FileInfo(path);
        return new SolidWorksArtifact(Guid.NewGuid().ToString("N"), "Test", path, extension, true, info.Length, "test");
    }

    private static string Write(string root, string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void AssertFailureReportTruth(SolidWorksWorkerResult result, string expectedFailureStage)
    {
        var reportArtifact = Assert.Single(result.GeneratedArtifacts, artifact =>
            artifact.ArtifactType.Equals("BuildReport", StringComparison.OrdinalIgnoreCase));
        using var report = JsonDocument.Parse(File.ReadAllText(reportArtifact.FilePath));
        Assert.False(report.RootElement.GetProperty("real_cad_connected").GetBoolean());
        Assert.False(report.RootElement.GetProperty("real_cad_executed").GetBoolean());
        Assert.Equal("Failed", report.RootElement.GetProperty("final_status").GetString());
        Assert.Equal(expectedFailureStage, report.RootElement.GetProperty("failure_stage").GetString());
    }

    private static string TempRoot(string name) =>
        Path.Combine(Path.GetTempPath(), "ai_me_v19_worker_tests", $"v19-{name}-{Guid.NewGuid():N}");

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

        throw new DirectoryNotFoundException();
    }

    private sealed class RecordingFamilyBuilder : TextPlaceholderPartFamilyBuilder
    {
        private readonly string _partType;
        private readonly string _mode;
        private readonly int _delayMilliseconds;

        public RecordingFamilyBuilder(string partType, string mode, int delayMilliseconds = 0)
        {
            _partType = partType;
            _mode = mode;
            _delayMilliseconds = delayMilliseconds;
        }

        public int BuildCount { get; private set; }
        public override string PartType => _partType;
        public override string FailureStage => "test_build_failed";
        public override bool SupportsRealExecution => true;
        public override string ApiEvidence => "test_api_evidence";
        public override string RealExecutionMode => _mode;

        public override async Task<PartFamilyBuildResult> BuildAsync(
            PartFamilyBuildContext context,
            CancellationToken cancellationToken = default)
        {
            BuildCount++;
            if (_delayMilliseconds > 0)
            {
                await Task.Delay(_delayMilliseconds, cancellationToken);
            }

            return new PartFamilyBuildResult("Completed", [], [], [], _mode, true);
        }
    }

    private sealed class CoordinatedSessionState
    {
        private int _active;
        private int _max;
        public int MaxConcurrentSessions => _max;

        public ISolidWorksSessionManager CreateManager() => new Session(this);

        private sealed class Session : ISolidWorksSessionManager
        {
            private readonly CoordinatedSessionState _state;
            private bool _connected;

            public Session(CoordinatedSessionState state) => _state = state;

            public Task<SolidWorksSessionConnectionResult> ConnectAsync(
                SolidWorksRuntimeOptions options,
                CancellationToken cancellationToken = default)
            {
                var active = Interlocked.Increment(ref _state._active);
                var observed = _state._max;
                while (active > observed)
                {
                    Interlocked.CompareExchange(ref _state._max, active, observed);
                    observed = _state._max;
                }

                _connected = true;
                return Task.FromResult(new SolidWorksSessionConnectionResult(true, "TestVersion", [], []));
            }

            public Task<T> ExecuteWithApplicationAsync<T>(
                Func<object, CancellationToken, Task<T>> action,
                CancellationToken cancellationToken = default,
                int? executionTimeoutSeconds = null) => action(new object(), cancellationToken);

            public Task DisconnectAsync(CancellationToken cancellationToken = default)
            {
                if (_connected)
                {
                    Interlocked.Decrement(ref _state._active);
                    _connected = false;
                }

                return Task.CompletedTask;
            }
        }
    }

    private sealed class FailingConnectionSessionManager : ISolidWorksSessionManager
    {
        public int ConnectAttempts { get; private set; }

        public Task<SolidWorksSessionConnectionResult> ConnectAsync(
            SolidWorksRuntimeOptions options,
            CancellationToken cancellationToken = default)
        {
            ConnectAttempts++;
            return Task.FromResult(new SolidWorksSessionConnectionResult(
                false,
                null,
                ["test_connection_attempted"],
                ["solidworks_connection_failed: test double rejected connection."]));
        }

        public Task<T> ExecuteWithApplicationAsync<T>(
            Func<object, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default,
            int? executionTimeoutSeconds = null) =>
            throw new InvalidOperationException("CAD execution must not start after a failed connection.");

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
