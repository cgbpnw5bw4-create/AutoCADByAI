using System.Text.Json;
using DomainSchemas;
using PlatformCore.Modules.CADModeling.Validators;
using SolidWorksWorker;
using SolidWorksWorker.Features;

namespace PlatformSelfCheck.Tests;

public sealed class V20CFeatureAdapterTests
{
    [Fact]
    public async Task RealAdapterExecutesAuthorizedSketchAndBlindExtrudeProfile()
    {
        var com = new AdapterComFacade { ReturnNullSketchFeature = true };
        using var adapter = new RealSolidWorksFeatureAdapter(com.Model, com);
        var state = new FeatureHandlerExecutionState();
        var sketch = Sketch("profile", Rectangle());
        var sketchOperation = Operation(
            "op-sketch",
            "CreateSketch",
            sketch,
            parameters: new Dictionary<string, string>
            {
                ["sketch_id"] = "profile",
                ["entities"] = sketch.Parameters["entities"],
                ["constraints"] = "[]",
                ["dimensions"] = "{}"
            });

        var sketchResult = await adapter.ExecuteSketchAsync(sketch, sketchOperation, state);
        var boss = new FeatureDefinition(
            "boss",
            FeatureTypes.ExtrudeBoss,
            new Dictionary<string, string>
            {
                ["depth_mm"] = "10",
                ["direction"] = "blind",
                ["sketch_id"] = "profile"
            },
            referencedSketches: ["profile"]);
        var bossResult = await adapter.ExecuteExtrudeBossAsync(
            boss,
            Operation("op-boss", "ExtrudeBoss", boss, ["op-sketch"]),
            state);

        Assert.True(sketchResult.IsSuccess, string.Join(Environment.NewLine, sketchResult.Issues));
        Assert.True(bossResult.IsSuccess, string.Join(Environment.NewLine, bossResult.Issues));
        Assert.IsType<FeatureAdapterArtifact>(sketchResult.CreatedObject);
        var artifact = Assert.IsType<FeatureAdapterArtifact>(bossResult.CreatedObject);
        Assert.True(artifact.ResultObjectValidated);
        Assert.True(artifact.RebuildPassed);
        Assert.Contains("FeatureExtrusion2", com.InvokedMethods);
        Assert.Contains(
            com.InvocationArguments["FeatureExtrusion2"],
            argument => argument is double depth && Math.Abs(depth - 0.01d) < 1e-12);
    }

    [Fact]
    public async Task NullFeatureResultIsRejectedAsFeatureResultInvalid()
    {
        var com = new AdapterComFacade { ReturnNullExtrude = true };
        using var adapter = new RealSolidWorksFeatureAdapter(com.Model, com);
        var state = new FeatureHandlerExecutionState();
        var sketch = Sketch("profile", Rectangle());
        Assert.True((await adapter.ExecuteSketchAsync(
            sketch,
            Operation(
                "op-sketch",
                "CreateSketch",
                sketch,
                parameters: new Dictionary<string, string>
                {
                    ["sketch_id"] = "profile",
                    ["entities"] = sketch.Parameters["entities"],
                    ["constraints"] = "[]",
                    ["dimensions"] = "{}"
                }),
            state)).IsSuccess);
        var boss = new FeatureDefinition(
            "boss",
            FeatureTypes.ExtrudeBoss,
            new Dictionary<string, string>
            {
                ["depth_mm"] = "10",
                ["direction"] = "blind",
                ["sketch_id"] = "profile"
            });

        var result = await adapter.ExecuteExtrudeBossAsync(
            boss,
            Operation("op-boss", "ExtrudeBoss", boss, ["op-sketch"]),
            state);

        Assert.False(result.IsSuccess);
        Assert.Equal(PartFamilyFailureStages.FeatureResultInvalid, result.FailureStage);
    }

    [Fact]
    public async Task RebuildFailureIsNotReportedAsSuccess()
    {
        var com = new AdapterComFacade { RebuildResult = false };
        using var adapter = new RealSolidWorksFeatureAdapter(com.Model, com);
        var sketch = Sketch("profile", Rectangle());

        var result = await adapter.ExecuteSketchAsync(
            sketch,
            Operation(
                "op-sketch",
                "CreateSketch",
                sketch,
                parameters: new Dictionary<string, string>
                {
                    ["sketch_id"] = "profile",
                    ["entities"] = sketch.Parameters["entities"],
                    ["constraints"] = "[]",
                    ["dimensions"] = "{}"
                }),
            new FeatureHandlerExecutionState());

        Assert.False(result.IsSuccess);
        Assert.Equal(PartFamilyFailureStages.FeatureResultInvalid, result.FailureStage);
        Assert.Null(result.CreatedObject);
    }

    [Fact]
    public async Task FeatureErrorValidationUsesDocumentedCompatibilityMemberWhenByRefDispatchIsUnavailable()
    {
        var com = new AdapterComFacade { ReturnNullErrorCode2 = true };
        using var adapter = new RealSolidWorksFeatureAdapter(com.Model, com);
        var state = new FeatureHandlerExecutionState();
        var sketch = Sketch("profile", Rectangle());
        var sketchOperation = Operation(
            "op-sketch",
            "CreateSketch",
            sketch,
            parameters: new Dictionary<string, string>
            {
                ["sketch_id"] = "profile",
                ["entities"] = sketch.Parameters["entities"],
                ["constraints"] = "[]",
                ["dimensions"] = "{}"
            });
        Assert.True((await adapter.ExecuteSketchAsync(sketch, sketchOperation, state)).IsSuccess);
        var boss = new FeatureDefinition(
            "boss",
            FeatureTypes.ExtrudeBoss,
            new Dictionary<string, string>
            {
                ["depth_mm"] = "10",
                ["direction"] = "blind"
            });

        var result = await adapter.ExecuteExtrudeBossAsync(
            boss,
            Operation("op-boss", "ExtrudeBoss", boss, ["op-sketch"]),
            state);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Issues));
        Assert.Contains("GetErrorCode", com.InvokedMethods);
    }

    [Fact]
    public async Task NonNullCutThatDoesNotChangeSolidVolumeIsRejected()
    {
        var com = new AdapterComFacade { DoNotChangeVolumeOnCut = true };
        using var adapter = new RealSolidWorksFeatureAdapter(com.Model, com);
        var state = new FeatureHandlerExecutionState();
        var bossSketch = Sketch("boss-profile", Rectangle());
        var bossSketchOperation = Operation(
            "op-boss-sketch",
            "CreateSketch",
            bossSketch,
            parameters: new Dictionary<string, string>
            {
                ["sketch_id"] = "boss-profile",
                ["entities"] = bossSketch.Parameters["entities"],
                ["constraints"] = "[]",
                ["dimensions"] = "{}"
            });
        Assert.True((await adapter.ExecuteSketchAsync(bossSketch, bossSketchOperation, state)).IsSuccess);
        var boss = new FeatureDefinition(
            "boss",
            FeatureTypes.ExtrudeBoss,
            new Dictionary<string, string>
            {
                ["depth_mm"] = "10",
                ["direction"] = "blind"
            });
        Assert.True((await adapter.ExecuteExtrudeBossAsync(
            boss,
            Operation("op-boss", "ExtrudeBoss", boss, ["op-boss-sketch"]),
            state)).IsSuccess);
        var cutSketch = Sketch(
            "cut-profile",
            new
            {
                entity_id = "circle",
                entity_type = SketchEntityTypes.Circle,
                parameters = new Dictionary<string, string>
                {
                    ["center_x_mm"] = "0",
                    ["center_y_mm"] = "0",
                    ["radius_mm"] = "5"
                }
            });
        var cutSketchOperation = Operation(
            "op-cut-sketch",
            "CreateSketch",
            cutSketch,
            parameters: new Dictionary<string, string>
            {
                ["sketch_id"] = "cut-profile",
                ["entities"] = cutSketch.Parameters["entities"],
                ["constraints"] = "[]",
                ["dimensions"] = "{}"
            });
        Assert.True((await adapter.ExecuteSketchAsync(cutSketch, cutSketchOperation, state)).IsSuccess);
        var cut = new FeatureDefinition(
            "cut",
            FeatureTypes.ExtrudeCut,
            new Dictionary<string, string>
            {
                ["depth_mm"] = "20",
                ["through_all"] = "false"
            });

        var result = await adapter.ExecuteExtrudeCutAsync(
            cut,
            Operation("op-cut", "CutExtrude", cut, ["op-boss", "op-cut-sketch"]),
            state);

        Assert.False(result.IsSuccess);
        Assert.Equal(PartFamilyFailureStages.FeatureResultInvalid, result.FailureStage);
        Assert.Contains(result.Issues, issue => issue.Contains("volume did not decrease", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerifiedEvidenceWithoutCompleteBindingDoesNotAllowRealExecution()
    {
        var evidence = new FeatureApiEvidence(
            "api",
            "official",
            [],
            "feature",
            [],
            FeatureApiEvidenceStatuses.Verified,
            [],
            []);

        Assert.False(evidence.AllowsRealExecution);
    }

    [Fact]
    public void ProductionEvidenceMatchesCurrentCompositeRevisionAndDiagnostic()
    {
        var registry = FeatureHandlerRegistry.CreateDefault();
        var expectedRevision = FeatureExecutionEvidencePolicy.ComputeCurrentSourceRevision();
        var features = new[]
        {
            Sketch("profile", Rectangle()),
            new FeatureDefinition(
                "boss",
                FeatureTypes.ExtrudeBoss,
                new Dictionary<string, string>
                {
                    ["depth_mm"] = "10",
                    ["direction"] = "blind"
                }),
            new FeatureDefinition(
                "cut",
                FeatureTypes.ExtrudeCut,
                new Dictionary<string, string>
                {
                    ["depth_mm"] = "20",
                    ["through_all"] = "false"
                }),
            new FeatureDefinition(
                "hole",
                FeatureTypes.Hole,
                new Dictionary<string, string>
                {
                    ["diameter_mm"] = "10",
                    ["depth_mm"] = "20",
                    ["strategy"] = "simple_circular_cut_blind"
                })
        };

        foreach (var feature in features)
        {
            var handler = registry.Resolve(feature).Handler!;
            Assert.Equal(expectedRevision, handler.ApiEvidence.SourceRevision);
            var evidence = handler.ValidateEvidenceForRealExecution(feature);
            Assert.True(evidence.IsValid, string.Join(Environment.NewLine, evidence.Issues));
            Assert.True(
                handler.ValidateRuntimeForRealExecution("31.5.0").IsValid);
        }
    }

    [Fact]
    public void VerifiedEvidenceRejectsRequestsOutsideItsExactParameterProfileBeforeCom()
    {
        var sketch = Sketch("front-profile", Rectangle());
        var plan = new SolidWorksBuildPlan(
            "outside-evidence-profile",
            "outside-evidence-spec",
            "SolidWorks",
            "plate_basic_4holes",
            "mm",
            [
                new SolidWorksOperation(
                    "front-sketch",
                    "CreateSketch",
                    "FrontPlane",
                    new Dictionary<string, string>
                    {
                        ["feature_id"] = sketch.FeatureId,
                        ["feature_type"] = sketch.FeatureType,
                        ["sketch_id"] = "front-profile",
                        ["entities"] = sketch.Parameters["entities"],
                        ["constraints"] = "[]",
                        ["dimensions"] = "{}"
                    },
                    [],
                    "test")
            ],
            [],
            [],
            [],
            ExecutionStrategy: SolidWorksBuildExecutionStrategies.FeatureHandlerGraph);

        var result = FeatureHandlerRegistry.CreateDefault().ValidateForRealExecution(plan);

        Assert.False(result.IsPassed);
        Assert.Equal(PartFamilyFailureStages.FeatureApiUnverified, result.FailureStage);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains("outside evidence profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenericArtifactValidatorRequiresFeatureEvidenceAndAcceptsCompleteReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "output", "solidworks", "real", $"v20-c-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var part = Write(root, "model.SLDPRT", "part");
            var step = Write(root, "model.STEP", MinimalStepContent);
            const string sourceRevision = "feature-execution-source-sha256:test";
            var diagnosticResult = new
            {
                feature_id = "profile",
                feature_type = "sketch",
                handler_name = "solidworks.sketch@2.0-c.2/diagnostic-candidate",
                api_evidence_status = "diagnostic_candidate",
                failure_stage = (string?)null,
                logs = Array.Empty<string>(),
                issues = Array.Empty<string>(),
                adapter_id = "solidworks.real-feature-adapter",
                adapter_version = "2.0-c.2",
                result_object_validated = true,
                rebuild_passed = true,
                evidence_id = "v20-c-diagnostic",
                evidence_handler_version = "2.0-c.2",
                evidence_parameter_profile = "test",
                evidence_solid_works_version = "33.5.0",
                evidence_diagnostic_run_path = "self",
                evidence_source_revision = sourceRevision,
                geometry_change_validated = true
            };
            var diagnosticPath = WriteJson(root, "diagnostic.json", new
            {
                candidate_only = true,
                main_workflow_accepted = false,
                quality_gate_passed = false,
                solid_works_connected = true,
                real_cad_executed = true,
                final_status = "CandidatePassed",
                deliverable_status = "NotDeliverable",
                solid_works_version = "33.5.0",
                feature_handler_reports = new[] { diagnosticResult }
            });
            var featureResult = new
            {
                feature_id = "profile",
                feature_type = "sketch",
                handler_name = "solidworks.sketch@2.0-c.2",
                api_evidence_status = "verified",
                failure_stage = (string?)null,
                logs = Array.Empty<string>(),
                issues = Array.Empty<string>(),
                adapter_id = "solidworks.real-feature-adapter",
                adapter_version = "2.0-c.2",
                result_object_validated = true,
                rebuild_passed = true,
                evidence_id = "v20-c-evidence",
                evidence_handler_version = "2.0-c.2",
                evidence_parameter_profile = "line_rectangle_circle_blind",
                evidence_solid_works_version = "33.5.0",
                evidence_diagnostic_run_path = diagnosticPath,
                evidence_source_revision = sourceRevision,
                geometry_change_validated = true,
                volume_before_cubic_meters = 0d,
                volume_after_cubic_meters = 0.00006d
            };
            var buildReport = WriteJson(root, "build_report.json", new
            {
                real_cad_executed = true,
                real_cad_connected = true,
                solidworks_version = "33.5.0",
                execution_mode = PartFamilyExecutionModes.GenericFeatureGraph,
                execution_strategy = SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                sldprt_save_success = true,
                step_export_success = true,
                final_status = "Passed",
                feature_handler_reports = new[] { featureResult }
            });
            var featureReport = WriteJson(root, "feature_execution_report.json", new
            {
                real_cad_executed = true,
                real_cad_connected = true,
                solidworks_version = "33.5.0",
                execution_mode = PartFamilyExecutionModes.GenericFeatureGraph,
                execution_strategy = SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                all_features_executed = true,
                all_result_objects_validated = true,
                all_rebuilds_passed = true,
                all_geometry_changes_validated = true,
                artifacts_validated = true,
                final_status = "Passed",
                feature_results = new[] { featureResult }
            });
            var result = new SolidWorksWorkerResult(
                "v20-c-validator",
                "Completed",
                [
                    Artifact("part", part, ".SLDPRT"),
                    Artifact("step", step, ".STEP"),
                    Artifact("build", buildReport, ".json"),
                    Artifact("feature", featureReport, ".json")
                ],
                [],
                [],
                PartFamilyExecutionModes.GenericFeatureGraph,
                RealCadExecuted: true,
                RealCadConnected: true);

            var review = new SolidWorksArtifactValidator(
                Path.Combine(Path.GetTempPath(), "output", "solidworks")).Validate(result);

            Assert.True(review.IsPassed, string.Join(Environment.NewLine, review.Issues));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GenericReleasePackageRequiresAndAcceptsSameRunFeatureReport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"v20-c-release-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var output = Path.Combine(root, "release");
        Directory.CreateDirectory(source);
        try
        {
            var part = Write(source, "model.SLDPRT", "part");
            var step = Write(source, "model.STEP", "step");
            var buildReport = WriteJson(source, "build_report.json", new
            {
                final_status = "Passed",
                failure_stage = (string?)null
            });
            var featureReport = WriteJson(source, "feature_execution_report.json", new
            {
                final_status = "Passed",
                failure_stage = (string?)null
            });
            var evidence = new[]
            {
                new SolidWorksReleaseExecutionEvidence(
                    "build",
                    "RealSolidWorksWorker",
                    PartFamilyExecutionModes.GenericFeatureGraph,
                    RealCadExecuted: true,
                    RealCadConnected: true,
                    QualityGatePassed: true,
                    FailureStage: null,
                    ReportPath: featureReport)
            };

            var result = await new SolidWorksE2EReleasePackageBuilder().BuildFromSourcesAsync(
                root,
                new SolidWorksReleasePackageSourceSet(
                    part,
                    step,
                    DrawingPath: null,
                    PdfPath: null,
                    BuildReportPath: buildReport,
                    DrawingReportPath: null,
                    DimensionReportPath: null,
                    TitleBlockReportPath: null,
                    ExecutionEvidence: evidence,
                    RequireDrawingDeliverables: false,
                    PartType: "model",
                    BuildExecutionStrategy: SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                    FeatureExecutionReportPath: featureReport),
                output);

            Assert.Equal("Completed", result.Status);
            Assert.True(File.Exists(Path.Combine(output, "reports", "feature_execution_report.json")));
            using var quality = JsonDocument.Parse(await File.ReadAllTextAsync(result.QualityReportPath!));
            Assert.True(quality.RootElement.GetProperty("real_execution_evidence_passed").GetBoolean());
            Assert.Equal("Deliverable", quality.RootElement.GetProperty("deliverable_status").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CadModelSpecValidatorAcceptsFeatureExecutionReportOutput()
    {
        var spec = new CADModelSpec(
            "v20-c-output",
            PlateBasic4HolesDefinition.Type,
            "mm",
            parameters: new Dictionary<string, string>
            {
                ["length_mm"] = "100",
                ["width_mm"] = "60",
                ["thickness_mm"] = "10",
                ["hole_count"] = "4",
                ["hole_diameter_mm"] = "10"
            },
            outputRequirements: ["SLDPRT", "STEP", "feature_execution_report.json"]);

        var result = new CADModelSpecValidator().Validate(spec);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues));
    }

    [Fact]
    public async Task UnknownFeatureParametersAreRejectedBeforeConnectAsync()
    {
        var session = new CountingSessionManager();
        var plan = Plan(
            "unknown-parameter",
            new SolidWorksOperation(
                "op-boss",
                "ExtrudeBoss",
                "TopPlane",
                new Dictionary<string, string>
                {
                    ["feature_id"] = "boss",
                    ["feature_type"] = FeatureTypes.ExtrudeBoss,
                    ["depth_mm"] = "10",
                    ["direction"] = "blind",
                    ["draft_angle"] = "2"
                },
                [],
                "test"));

        var result = await new RealSolidWorksWorker(session, RuntimeOptions())
            .ExecuteAsync(new SolidWorksWorkerRequest(
                "unknown-parameter",
                plan,
                Path.GetTempPath(),
                DryRun: false));

        Assert.Equal("Rejected", result.Status);
        Assert.Equal(0, session.ConnectCount);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains("draft_angle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HoleDiameterMustMatchItsSingleDependencyCircleBeforeConnectAsync()
    {
        var session = new CountingSessionManager();
        var sketch = Sketch(
            "hole-profile",
            new
            {
                entity_id = "circle",
                entity_type = SketchEntityTypes.Circle,
                parameters = new Dictionary<string, string>
                {
                    ["center_x_mm"] = "0",
                    ["center_y_mm"] = "0",
                    ["radius_mm"] = "5"
                }
            });
        var sketchOperation = new SolidWorksOperation(
            "op-sketch",
            "CreateSketch",
            "TopPlane",
            new Dictionary<string, string>
            {
                ["feature_id"] = "hole-profile",
                ["feature_type"] = FeatureHandlerTypes.Sketch,
                ["sketch_id"] = "hole-profile",
                ["entities"] = sketch.Parameters["entities"],
                ["constraints"] = "[]",
                ["dimensions"] = "{}"
            },
            [],
            "test");
        var holeOperation = new SolidWorksOperation(
            "op-hole",
            "CreateSimpleHole",
            "TopPlane",
            new Dictionary<string, string>
            {
                ["feature_id"] = "hole",
                ["feature_type"] = FeatureTypes.Hole,
                ["sketch_id"] = "hole-profile",
                ["referenced_sketches"] = """["hole-profile"]""",
                ["diameter_mm"] = "12",
                ["depth_mm"] = "20",
                ["strategy"] = "simple_circular_cut_blind"
            },
            ["op-sketch"],
            "test");

        var result = await new RealSolidWorksWorker(session, RuntimeOptions())
            .ExecuteAsync(new SolidWorksWorkerRequest(
                "hole-mismatch",
                Plan("hole-mismatch", sketchOperation, holeOperation),
                Path.GetTempPath(),
                DryRun: false));

        Assert.Equal("Rejected", result.Status);
        Assert.Equal(0, session.ConnectCount);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(
                "diameter_matches_single_circle",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StaleCompositeSourceRevisionIsRejectedBeforeConnectAsync()
    {
        var session = new CountingSessionManager();
        var diagnostic = Path.GetTempFileName();
        try
        {
            var registry = new FeatureHandlerRegistry(
                [new StaleEvidenceHandler(diagnostic)]);
            var worker = new RealSolidWorksWorker(
                session,
                RuntimeOptions(),
                null,
                null,
                null,
                null,
                null,
                null,
                registry);
            var operation = new SolidWorksOperation(
                "op-stale",
                "ProbeOperation",
                "TopPlane",
                new Dictionary<string, string>
                {
                    ["feature_id"] = "stale",
                    ["feature_type"] = "stale_probe"
                },
                [],
                "test");

            var result = await worker.ExecuteAsync(new SolidWorksWorkerRequest(
                "stale-evidence",
                Plan("stale-evidence", operation),
                Path.GetTempPath(),
                DryRun: false));

            Assert.Equal("Rejected", result.Status);
            Assert.Equal(0, session.ConnectCount);
            Assert.Contains(
                result.Issues,
                issue => issue.Contains("stale source_revision", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(diagnostic);
        }
    }

    private static FeatureDefinition Sketch(string id, object entity)
    {
        var entities = JsonSerializer.Serialize(new[] { entity });
        return new FeatureDefinition(
            id,
            FeatureHandlerTypes.Sketch,
            new Dictionary<string, string> { ["entities"] = entities },
            targetReference: "TopPlane");
    }

    private static object Rectangle() => new
    {
        entity_id = "rectangle",
        entity_type = "rectangle",
        parameters = new Dictionary<string, string>
        {
            ["center_x_mm"] = "0",
            ["center_y_mm"] = "0",
            ["width_mm"] = "100",
            ["height_mm"] = "60"
        }
    };

    private static SolidWorksOperation Operation(
        string operationId,
        string operationType,
        FeatureDefinition feature,
        IReadOnlyList<string>? dependencies = null,
        IReadOnlyDictionary<string, string>? parameters = null) =>
        new(
            operationId,
            operationType,
            "TopPlane",
            parameters ?? new Dictionary<string, string>(feature.Parameters)
            {
                ["feature_id"] = feature.FeatureId,
                ["feature_type"] = feature.FeatureType
            },
            dependencies ?? [],
            "test");

    private static string Write(string root, string name, string value)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, value);
        return path;
    }

    private const string MinimalStepContent =
        "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n";

    private static string WriteJson(string root, string name, object value) =>
        Write(
            root,
            name,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    private static SolidWorksArtifact Artifact(string id, string path, string extension)
    {
        var info = new FileInfo(path);
        return new SolidWorksArtifact(id, id, path, extension, info.Exists, info.Length, id);
    }

    private static SolidWorksBuildPlan Plan(
        string id,
        params SolidWorksOperation[] operations) =>
        new(
            id,
            $"{id}-spec",
            "SolidWorks",
            "plate_basic_4holes",
            "mm",
            operations,
            [],
            [],
            [],
            ExecutionStrategy: SolidWorksBuildExecutionStrategies.FeatureHandlerGraph);

    private static SolidWorksRuntimeOptions RuntimeOptions() =>
        new(
            EnableRealExecution: true,
            Visible: false,
            TemplatePartPath: null,
            OutputDirectory: Path.GetTempPath(),
            ConnectTimeoutSeconds: 1,
            ExecutionTimeoutSeconds: 1,
            MainWorkflowExecutionEnabled: true,
            RealExecutionDefaultEnabled: true,
            DisableRealExecution: false,
            IsCiEnvironment: false,
            IsUnitTestEnvironment: false,
            VisibleModeDefault: true,
            ForceFakeWorker: false);

    private sealed class CountingSessionManager : ISolidWorksSessionManager
    {
        public int ConnectCount { get; private set; }

        public Task<SolidWorksSessionConnectionResult> ConnectAsync(
            SolidWorksRuntimeOptions options,
            CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            throw new InvalidOperationException("ConnectAsync must not be called.");
        }

        public Task<T> ExecuteWithApplicationAsync<T>(
            Func<object, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default,
            int? executionTimeoutSeconds = null) =>
            throw new InvalidOperationException("Application execution must not be called.");

        public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StaleEvidenceHandler(string diagnosticPath) : FeatureHandlerBase
    {
        public override string FeatureType => "stale_probe";
        public override string OperationType => "ProbeOperation";
        public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema => [];
        public override FeatureApiEvidence ApiEvidence { get; } = new(
            "probe",
            "test",
            [],
            "probe",
            [],
            FeatureApiEvidenceStatuses.Verified,
            [],
            [],
            EvidenceId: "stale-evidence",
            HandlerVersion: "2.0-c.2",
            ParameterProfile: "probe",
            SolidWorksVersion: "33.5.0",
            DiagnosticRunPath: diagnosticPath,
            SourceRevision: "feature-execution-source-sha256:stale");

        public override FeatureHandlerValidationResult Validate(FeatureDefinition feature) =>
            FeatureHandlerValidationResult.Passed();

        public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
            FeatureHandlerExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EvidenceBlocked(context.Feature));
    }

    private sealed class AdapterComFacade : ISolidWorksComFacade
    {
        private readonly object _extension = new();
        private readonly object _sketchManager = new();
        private readonly object _selectionManager = new();
        private readonly object _featureManager = new();
        private readonly object _activeSketch = new();
        private readonly object _sketchFeature = new();
        private readonly object _feature = new();
        private readonly object _body = new();
        private double _solidVolume;

        public object Model { get; } = new();
        public bool ReturnNullSketchFeature { get; init; }
        public bool ReturnNullExtrude { get; init; }
        public bool ReturnNullErrorCode2 { get; init; }
        public bool DoNotChangeVolumeOnCut { get; init; }
        public bool RebuildResult { get; init; } = true;
        public List<string> InvokedMethods { get; } = [];
        public Dictionary<string, object?[]> InvocationArguments { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public object GetProperty(object target, string name) =>
            TryGetProperty(target, name) ?? throw new InvalidOperationException(name);

        public object? TryGetProperty(object? target, string name) =>
            name switch
            {
                "Extension" => _extension,
                "SketchManager" => _sketchManager,
                "SelectionManager" => _selectionManager,
                "FeatureManager" => _featureManager,
                "ActiveSketch" => _activeSketch,
                _ => null
            };

        public object? TryGetIndexedProperty(object target, string name, params object?[] args) => null;

        public object? Invoke(object target, string name, params object?[] args) =>
            InvokeWithArgs(target, name, args);

        public object? InvokeWithArgs(object target, string name, object?[] args)
        {
            InvokedMethods.Add(name);
            InvocationArguments[name] = args;
            return name switch
            {
                "InsertSketch" => null,
                "ClearSelection2" => true,
                "CreateLine" => new object(),
                "CreateCircle" => new object(),
                "CreateCenterRectangle" => new object[] { new(), new(), new(), new() },
                "FeatureExtrusion2" => CreateExtrude(),
                "FeatureCut4" => CreateCut(),
                _ => null
            };
        }

        public object? TryInvoke(object? target, string name, params object?[] args)
        {
            InvokedMethods.Add(name);
            return name switch
            {
                "GetActiveSketch2" => _activeSketch,
                "GetFeature" => ReturnNullSketchFeature ? null : _sketchFeature,
                "GetSelectedObjectCount2" => 1,
                "GetSelectedObject6" => new object(),
                "GetErrorCode" => 0,
                "GetBodies2" => _solidVolume > 0d ? new[] { _body } : null,
                "GetMassProperties" => new[] { 0d, 0d, 0d, _solidVolume },
                _ => InvokeWithArgs(target ?? new object(), name, args)
            };
        }

        public object? TryInvokeWithArgs(object? target, string name, object?[] args)
        {
            if (name.Equals("GetErrorCode2", StringComparison.OrdinalIgnoreCase))
            {
                if (ReturnNullErrorCode2)
                {
                    InvokedMethods.Add(name);
                    return null;
                }

                args[0] = false;
                return 0;
            }

            return TryInvoke(target, name, args);
        }

        public bool TryInvokeBool(object? target, string name, params object?[] args) =>
            name.Equals("ForceRebuild3", StringComparison.OrdinalIgnoreCase)
                ? RebuildResult
                : name is "SelectByID2" or "Select2";

        public bool TrySetProperty(object target, string name, object? value) => true;

        public bool TryExtensionSaveAs(
            object model,
            string path,
            object? exportData,
            List<string> errors,
            List<string> warnings) => false;

        public void ReleaseComObject(object value)
        {
        }

        private object? CreateExtrude()
        {
            if (ReturnNullExtrude)
            {
                return null;
            }

            _solidVolume = 0.00006d;
            return _feature;
        }

        private object CreateCut()
        {
            if (!DoNotChangeVolumeOnCut)
            {
                _solidVolume -= 0.0000008d;
            }

            return _feature;
        }
    }
}
