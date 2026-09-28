using System.Collections.Concurrent;
using System.Text.Json;
using DomainSchemas;
using PlatformCore.Modules.CADModeling.Reviewers;
using PlatformCore.Modules.CADModeling.Validators;
using SolidWorksPartFamilySmokeRunner;
using SolidWorksWorker;
using SolidWorksWorker.Features;

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
        Assert.Equal("jacket_profile_create_failed", PartFamilyFailureStages.JacketProfileCreateFailed);
        Assert.Equal("jacket_extrude_failed", PartFamilyFailureStages.JacketExtrudeFailed);
        Assert.Equal("jacket_inner_cut_failed", PartFamilyFailureStages.JacketInnerCutFailed);
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
    public void JacketDefinitionCompilesReviewedTwoFeatureShellPlan()
    {
        var spec = new CADModelSpec(
            "jacket-review",
            JacketBasicDefinition.Type,
            new Dictionary<string, string>
            {
                ["outer_diameter_mm"] = "140",
                ["inner_diameter_mm"] = "120",
                ["length_mm"] = "180"
            });
        var definition = new JacketBasicDefinition();
        var plan = Assert.IsType<SolidWorksBuildPlan>(definition.GenerateBuildPlan("jacket-review", spec).BuildPlan);

        Assert.True(new SolidWorksBuildPlanReviewer().Review(plan).IsPassed);
        Assert.Equal(PartFamilyExecutionModes.GenericFeatureGraph, definition.RealExecutionMode);
        Assert.Equal(
            ["CreateSketch", "ExtrudeBoss", "CreateSketch", "CutExtrude", "SavePart", "ExportStep"],
            plan.Operations.Select(operation => operation.OperationType));
        Assert.Equal("TopPlane", plan.Operations[2].SketchPlane);
        Assert.Equal("blind", plan.Operations[3].Parameters["direction"]);
        Assert.Equal("false", plan.Operations[3].Parameters["through_all"]);
        Assert.Equal("360", plan.Operations[3].Parameters["depth_mm"]);
    }

    [Fact]
    public void JacketValidatorRejectsInvalidWallGeometry()
    {
        var result = new JacketBasicValidator().Validate(new CADModelSpec(
            "invalid-jacket",
            JacketBasicDefinition.Type,
            new Dictionary<string, string>
            {
                ["outer_diameter_mm"] = "120",
                ["inner_diameter_mm"] = "120",
                ["length_mm"] = "180"
            }));

        Assert.False(result.IsValid);
        Assert.Equal(PartFamilyFailureStages.InvalidParameterValue, result.FailureStage);
    }

    [Theory]
    [InlineData("120", "118.2", "180")]
    [InlineData("120", "-1", "180")]
    [InlineData("NaN", "100", "180")]
    [InlineData("120", "100", "-1")]
    public void JacketValidatorRejectsThinNonFiniteOrNonPositiveGeometry(
        string outerDiameter,
        string innerDiameter,
        string length)
    {
        var result = new JacketBasicValidator().Validate(new CADModelSpec(
            "invalid-jacket-boundary",
            JacketBasicDefinition.Type,
            new Dictionary<string, string>
            {
                ["outer_diameter_mm"] = outerDiameter,
                ["inner_diameter_mm"] = innerDiameter,
                ["length_mm"] = length
            }));

        Assert.False(result.IsValid);
        Assert.Equal(PartFamilyFailureStages.InvalidParameterValue, result.FailureStage);
    }

    [Fact]
    public void JacketGeometryValidatorRejectsMeasuredVolumeDeviation()
    {
        var measured = ValidJacketGeometry() with { VolumeCubicMillimeters = 100d };

        var result = JacketGeometryValidator.Validate(140d, 120d, 180d, measured);

        Assert.False(result.IsValid);
        Assert.Equal(PartFamilyFailureStages.VolumeValidationFailed, result.FailureStage);
        Assert.Contains(result.Issues, issue => issue.Contains("Expected", StringComparison.Ordinal));
    }

    [Fact]
    public void StrictFinalRebuildIsThePlatformDefaultAndNoRegisteredBuilderRelaxesIt()
    {
        // A builder that adds nothing must still be strict: the guard belongs to
        // the platform, not to whichever family remembered to opt in.
        Assert.True(new DefaultRebuildPolicyProbe().StrictFinalRebuild);

        var inspected = new List<string>();
        var relaxed = new List<string>();
        foreach (var builder in PartFamilyBuilderRegistry.CreateDefault().GetAll())
        {
            if (builder is not SolidWorksPartFamilyBuilderBase)
            {
                continue;
            }

            var property = StrictFinalRebuildProperty(builder.GetType());
            Assert.NotNull(property);
            inspected.Add(builder.PartType);
            if (property!.GetValue(builder) is not true)
            {
                relaxed.Add(builder.PartType);
            }
        }

        Assert.NotEmpty(inspected);
        Assert.Empty(relaxed);
    }

    private static System.Reflection.PropertyInfo? StrictFinalRebuildProperty(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(
                "RequiresStrictFinalRebuild",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private sealed class DefaultRebuildPolicyProbe : SolidWorksPartFamilyBuilderBase
    {
        public DefaultRebuildPolicyProbe()
            : base(null, null, null)
        {
        }

        public override string PartType => "rebuild_policy_probe";

        public override string FailureStage => "rebuild_policy_probe_failed";

        public override bool SupportsRealExecution => false;

        public override string ApiEvidence => "rebuild_policy_probe";

        public bool StrictFinalRebuild => RequiresStrictFinalRebuild;

        protected override void BuildFeatures(
            object model,
            SolidWorksBuildPlan plan,
            SolidWorksPartFamilyBuildDiagnostics diagnostics,
            List<string> logs)
        {
        }
    }

    [Fact]
    public async Task JacketBuilderRejectsFalseFinalRebuildAndWritesFailureReport()
    {
        var root = TempRoot("jacket-rebuild-false");
        try
        {
            var builder = new JacketFeatureBuilder(
                new JacketComFacade(rebuildResult: false),
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()));

            var result = await builder.BuildAsync(JacketContext(root));

            Assert.Equal("Failed", result.Status);
            Assert.Equal(PartFamilyFailureStages.FeatureResultInvalid, result.FailureStage);
            Assert.DoesNotContain(result.Artifacts, artifact =>
                artifact.FilePath.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase));
            AssertFailedBuildReport(result, PartFamilyFailureStages.FeatureResultInvalid);
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
    public async Task JacketBuilderCompletesOnlyAfterGeometryAndStepContentValidation()
    {
        var root = Path.Combine(
            FindProjectRoot(),
            "output",
            "solidworks",
            "real",
            $"v21-jacket-controlled-success-{Guid.NewGuid():N}");
        try
        {
            var builder = new JacketFeatureBuilder(
                new JacketComFacade(),
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()));

            var result = await builder.BuildAsync(JacketContext(root));

            Assert.Equal("Completed", result.Status);
            var reportArtifact = Assert.Single(result.Artifacts, artifact =>
                artifact.ArtifactType.Equals("BuildReport", StringComparison.OrdinalIgnoreCase));
            using var report = JsonDocument.Parse(File.ReadAllText(reportArtifact.FilePath));
            Assert.Equal("Passed", report.RootElement.GetProperty("final_status").GetString());
            Assert.True(report.RootElement.GetProperty("step_content_validated").GetBoolean());
            Assert.Equal("Passed", report.RootElement.GetProperty("geometry_validation_status").GetString());
            Assert.Equal(1, report.RootElement.GetProperty("measured_body_count").GetInt32());
            Assert.True(
                report.RootElement.GetProperty("measured_volume_cubic_mm").GetDouble() > 735_000d);

        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("CreateCircle:1", PartFamilyFailureStages.JacketProfileCreateFailed)]
    [InlineData("FeatureExtrusion2", PartFamilyFailureStages.JacketExtrudeFailed)]
    [InlineData("CreateCircle:2", PartFamilyFailureStages.JacketInnerCutFailed)]
    [InlineData("FeatureCut4", PartFamilyFailureStages.JacketInnerCutFailed)]
    public async Task JacketBuilderRejectsNullComResultsAtPreciseStage(
        string failAt,
        string expectedStage)
    {
        var root = TempRoot($"jacket-null-{failAt.Replace(':', '-')}");
        try
        {
            var builder = new JacketFeatureBuilder(
                new JacketComFacade(failAt: failAt),
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()));

            var result = await builder.BuildAsync(JacketContext(root));

            Assert.Equal("Failed", result.Status);
            Assert.Equal(expectedStage, result.FailureStage);
            AssertFailedBuildReport(result, expectedStage);
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
    public async Task JacketBuilderRejectsNonStepExportContent()
    {
        var root = TempRoot("jacket-invalid-step");
        try
        {
            var builder = new JacketFeatureBuilder(
                new JacketComFacade(stepContent: "native SolidWorks bytes"),
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()));

            var result = await builder.BuildAsync(JacketContext(root));

            Assert.Equal("Failed", result.Status);
            Assert.Equal(PartFamilyFailureStages.StepExportFailed, result.FailureStage);
            Assert.Contains(result.Issues, issue =>
                issue.Contains("ISO-10303-21", StringComparison.Ordinal));
            AssertFailedBuildReport(result, PartFamilyFailureStages.StepExportFailed);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("current-target", 1)]
    [InlineData("activate-target", 1)]
    [InlineData("initial-active-null", 1)]
    public async Task StepExportRequiresSavedDocumentIdentityAndExportsOriginalModel(
        string identityScenario,
        int expectedActivationCount)
    {
        var root = Path.Combine(
            FindProjectRoot(), "output", "solidworks", "real", $"step-identity-success-{Guid.NewGuid():N}");
        try
        {
            var com = new JacketComFacade(identityScenario: identityScenario);
            var builder = new JacketFeatureBuilder(
                com,
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()));

            var result = await builder.BuildAsync(JacketContext(root));

            Assert.Equal("Completed", result.Status);
            Assert.Equal(expectedActivationCount, com.ActivationCount);
            Assert.Same(com.OriginalModel, Assert.Single(com.StepExportTargets));
            Assert.Equal("Extension.SaveAs", Assert.Single(com.StepSaveMethods));
            Assert.Contains(result.Logs, log => log.Contains("step_export_document_identity_verified", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.ChangeExtension(com.SavedPartPath!, ".STEP")));
            Assert.Equal(Path.GetFileName(com.SavedPartPath), Assert.Single(com.ClosedDocumentNames));
            Assert.DoesNotContain("jacket_basic.SLDPRT", com.ClosedDocumentNames);
            Assert.Equal("jacket_basic.SLDPRT", Path.GetFileName(Assert.Single(result.Artifacts, artifact => artifact.ArtifactType == "Part").FilePath));
            Assert.Equal("jacket_basic.STEP", Path.GetFileName(Assert.Single(result.Artifacts, artifact => artifact.ArtifactType == "Step").FilePath));
            if (expectedActivationCount == 1)
            {
                Assert.Equal(Path.GetFileName(com.SavedPartPath), com.ActivationArguments![0]);
                Assert.Equal(false, com.ActivationArguments[1]);
                Assert.Equal(1, com.ActivationArguments[2]);
                Assert.Equal(0, com.ActivationArguments[3]);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FamilyRunsUseDistinctOwnedNamesAndPublishIdenticalBytesWithStableNames()
    {
        var root = TempRoot("owned-document-compatible-names");
        try
        {
            var internalNames = new List<string>();
            foreach (var run in new[] { "first", "second" })
            {
                var com = new JacketComFacade();
                var result = await new JacketFeatureBuilder(
                    com,
                    planeSelector: new AlwaysSelectedPlaneSelector(),
                    geometryReader: new FixedGeometryReader(ValidJacketGeometry()))
                    .BuildAsync(JacketContext(Path.Combine(root, run)));

                Assert.Equal("Completed", result.Status);
                internalNames.Add(Path.GetFileName(com.SavedPartPath!));
                Assert.Equal(internalNames[^1], Assert.Single(com.ClosedDocumentNames));
                var part = Assert.Single(result.Artifacts, artifact => artifact.ArtifactType == "Part");
                var step = Assert.Single(result.Artifacts, artifact => artifact.ArtifactType == "Step");
                Assert.Equal("jacket_basic.SLDPRT", Path.GetFileName(part.FilePath));
                Assert.Equal("jacket_basic.STEP", Path.GetFileName(step.FilePath));
                Assert.NotEqual(com.SavedPartPath, part.FilePath);
                Assert.Equal(File.ReadAllBytes(com.SavedPartPath!), File.ReadAllBytes(part.FilePath));
                Assert.Equal(File.ReadAllBytes(Path.ChangeExtension(com.SavedPartPath, ".STEP")!), File.ReadAllBytes(step.FilePath));
                using var report = JsonDocument.Parse(File.ReadAllText(
                    Assert.Single(result.Artifacts, artifact => artifact.ArtifactType == "BuildReport").FilePath));
                Assert.Equal(part.FilePath, report.RootElement.GetProperty("sldprt_path").GetString());
                Assert.Equal(step.FilePath, report.RootElement.GetProperty("step_path").GetString());
            }
            Assert.NotEqual(internalNames[0], internalNames[1]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FamilyDoesNotPublishStableNamesUntilOwnedDocumentCloses()
    {
        var root = TempRoot("owned-document-close-failed");
        try
        {
            var com = new JacketComFacade(closeDocThrows: true);
            var result = await new JacketFeatureBuilder(
                com,
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()))
                .BuildAsync(JacketContext(root));

            Assert.Equal("Failed", result.Status);
            Assert.Equal(PartFamilyFailureStages.ArtifactValidationFailed, result.FailureStage);
            Assert.Empty(com.ClosedDocumentNames);
            Assert.DoesNotContain(result.Artifacts, artifact => artifact.ArtifactType is "Part" or "Step");
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(com.SavedPartPath!)!, "jacket_basic.SLDPRT")));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(com.SavedPartPath!)!, "jacket_basic.STEP")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FamilyNeverOverwritesExistingStableDeliverables()
    {
        var root = Path.Combine(TempRoot("owned-document-no-overwrite"), JacketBasicDefinition.Type);
        Directory.CreateDirectory(root);
        var existingPart = Write(root, "jacket_basic.SLDPRT", "原有零件");
        var existingStep = Write(root, "jacket_basic.STEP", "原有 STEP");
        try
        {
            var com = new JacketComFacade();
            var result = await new JacketFeatureBuilder(
                com,
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()))
                .BuildAsync(JacketContext(root));

            Assert.Equal("Failed", result.Status);
            Assert.Equal(PartFamilyFailureStages.ArtifactValidationFailed, result.FailureStage);
            Assert.Equal("原有零件", File.ReadAllText(existingPart));
            Assert.Equal("原有 STEP", File.ReadAllText(existingStep));
            Assert.Equal(Path.GetFileName(com.SavedPartPath), Assert.Single(com.ClosedDocumentNames));
            Assert.DoesNotContain(result.Artifacts, artifact => artifact.ArtifactType is "Part" or "Step");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("same-title-wrong-document", "step_export_document_identity_invalid")]
    [InlineData("current-target-activation-wrong", "step_export_document_identity_invalid")]
    [InlineData("returned-wrong-document", "step_export_document_identity_invalid")]
    [InlineData("active-wrong-document", "step_export_document_identity_invalid")]
    [InlineData("active-null", "step_export_document_identity_invalid")]
    [InlineData("returned-null", "step_export_activation_failed")]
    [InlineData("activation-error", "step_export_activation_failed")]
    [InlineData("activation-rebuild-warning", "step_export_activation_failed")]
    [InlineData("activation-error-unwritten", "step_export_activation_failed")]
    [InlineData("missing-returned-path", "step_export_document_identity_invalid")]
    [InlineData("missing-original-path", "step_export_document_identity_invalid")]
    [InlineData("relative-original-path", "step_export_document_identity_invalid")]
    [InlineData("wrong-original-path", "step_export_document_identity_invalid")]
    public async Task StepExportRejectsAmbiguousOrFailedActivationBeforeEverySaveApi(
        string identityScenario,
        string expectedIssue)
    {
        var root = TempRoot($"step-identity-{identityScenario}");
        try
        {
            var com = new JacketComFacade(identityScenario: identityScenario);
            var builder = new JacketFeatureBuilder(
                com,
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()));

            var result = await builder.BuildAsync(JacketContext(root));

            Assert.Equal("Failed", result.Status);
            Assert.Equal(PartFamilyFailureStages.StepExportFailed, result.FailureStage);
            Assert.Contains(result.Issues, issue => issue.Contains(expectedIssue, StringComparison.Ordinal));
            Assert.NotNull(com.SavedPartPath);
            Assert.True(File.Exists(com.SavedPartPath));
            Assert.Empty(com.StepExportTargets);
            Assert.Empty(com.StepSaveMethods);
            Assert.False(File.Exists(Path.ChangeExtension(com.SavedPartPath, ".STEP")));
            Assert.DoesNotContain(result.Artifacts, artifact => artifact.ArtifactType == "Step");
            AssertFailedBuildReport(result, PartFamilyFailureStages.StepExportFailed);
            if (identityScenario.EndsWith("original-path", StringComparison.Ordinal))
            {
                Assert.Equal(0, com.ActivationCount);
                Assert.Empty(com.ClosedDocumentNames);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("current-target", true)]
    [InlineData("activate-target", true)]
    [InlineData("same-title-wrong-document", false)]
    [InlineData("active-null", false)]
    [InlineData("returned-null", false)]
    [InlineData("activation-rebuild-warning", false)]
    public void LegacyPlateStepExporterUsesTheSameDocumentIdentityBoundary(
        string identityScenario,
        bool expectedSuccess)
    {
        var root = TempRoot($"plate-step-identity-{identityScenario}");
        try
        {
            var com = new JacketComFacade(identityScenario: identityScenario);
            var partPath = Path.Combine(root, "jacket_basic.SLDPRT");
            var stepPath = Path.ChangeExtension(partPath, ".STEP");
            Assert.True(com.TryExtensionSaveAs(com.OriginalModel, partPath, null, [], []));
            var diagnostics = new SolidWorksPlateBuildDiagnostics { SldprtPath = partPath };
            var builder = new LateBoundSolidWorksPlateBuilder(com);
            // 旧兼容出口没有独立公开接口；只隔离此前已完成的建模，直接验证实际导出边界。
            var export = typeof(LateBoundSolidWorksPlateBuilder).GetMethod(
                "ExportStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            void Export() => export.Invoke(builder, [new object(), com.OriginalModel, stepPath, diagnostics, new List<string>()]);

            if (expectedSuccess)
            {
                Export();
                Assert.True(diagnostics.StepExportSuccess);
                Assert.Same(com.OriginalModel, Assert.Single(com.StepExportTargets));
                Assert.Equal("Extension.SaveAs", Assert.Single(com.StepSaveMethods));
                Assert.True(File.Exists(stepPath));
                Assert.Equal(1, com.ActivationCount);
            }
            else
            {
                var exception = Assert.Throws<System.Reflection.TargetInvocationException>(Export);
                Assert.IsType<IOException>(exception.InnerException);
                Assert.True(diagnostics.StepExportAttempted);
                Assert.False(diagnostics.StepExportSuccess);
                Assert.NotEmpty(diagnostics.StepExportErrors);
                Assert.Empty(com.StepExportTargets);
                Assert.Empty(com.StepSaveMethods);
                Assert.False(File.Exists(stepPath));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 8, 4)]
    [InlineData(true, 8, 0)]
    [InlineData(true, 0, 4)]
    public async Task FamilyStepExportCannotMaskExtensionFailureWithAnotherSaveApi(
        bool extensionResult,
        int extensionError,
        int extensionWarning)
    {
        var root = TempRoot("step-extension-result");
        try
        {
            var com = new JacketComFacade(
                stepExportResult: extensionResult, stepExportError: extensionError, stepExportWarning: extensionWarning);
            var builder = new JacketFeatureBuilder(
                com,
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()));

            var result = await builder.BuildAsync(JacketContext(root));

            var expectedSuccess = extensionResult && extensionError == 0;
            Assert.Equal(expectedSuccess ? "Completed" : "Failed", result.Status);
            Assert.Equal("Extension.SaveAs", Assert.Single(com.StepSaveMethods));
            Assert.Same(com.OriginalModel, Assert.Single(com.StepExportTargets));
            // 即使失败接口留下非空文件，也不能凭文件存在将执行宣称成功。
            Assert.True(File.Exists(Path.ChangeExtension(com.SavedPartPath, ".STEP")));
            if (!expectedSuccess)
            {
                Assert.Equal(PartFamilyFailureStages.StepExportFailed, result.FailureStage);
                Assert.DoesNotContain(result.Artifacts, artifact => artifact.ArtifactType == "Step");
                AssertFailedBuildReport(result, PartFamilyFailureStages.StepExportFailed);
            }
            if (extensionError != 0)
            {
                Assert.Contains(result.Issues, issue => issue.Contains($"save_as_errors: {extensionError}", StringComparison.Ordinal));
            }
            var reportArtifact = Assert.Single(result.Artifacts, artifact => artifact.ArtifactType == "BuildReport");
            using var report = JsonDocument.Parse(File.ReadAllText(reportArtifact.FilePath));
            if (extensionWarning != 0)
            {
                Assert.Contains(report.RootElement.GetProperty("warnings").EnumerateArray(),
                    warning => warning.GetString() == $"save_as_warnings: {extensionWarning}");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 8, 4)]
    [InlineData(true, 8, 0)]
    [InlineData(true, 0, 4)]
    public void LegacyPlateStepExportCannotMaskExtensionFailureWithAnotherSaveApi(
        bool extensionResult,
        int extensionError,
        int extensionWarning)
    {
        var root = TempRoot("plate-step-extension-result");
        try
        {
            var com = new JacketComFacade(
                stepExportResult: extensionResult, stepExportError: extensionError, stepExportWarning: extensionWarning);
            var partPath = Path.Combine(root, "jacket_basic.SLDPRT");
            var stepPath = Path.ChangeExtension(partPath, ".STEP");
            Assert.True(com.TryExtensionSaveAs(com.OriginalModel, partPath, null, [], []));
            var diagnostics = new SolidWorksPlateBuildDiagnostics { SldprtPath = partPath };
            var builder = new LateBoundSolidWorksPlateBuilder(com);
            var export = typeof(LateBoundSolidWorksPlateBuilder).GetMethod(
                "ExportStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            void Export() => export.Invoke(builder, [new object(), com.OriginalModel, stepPath, diagnostics, new List<string>()]);

            var expectedSuccess = extensionResult && extensionError == 0;
            if (expectedSuccess) Export();
            else Assert.IsType<IOException>(Assert.Throws<System.Reflection.TargetInvocationException>(Export).InnerException);

            Assert.Equal(expectedSuccess, diagnostics.StepExportSuccess);
            Assert.Equal("Extension.SaveAs", Assert.Single(com.StepSaveMethods));
            Assert.Same(com.OriginalModel, Assert.Single(com.StepExportTargets));
            Assert.True(File.Exists(stepPath));
            if (extensionError != 0) Assert.Contains($"save_as_errors: {extensionError}", diagnostics.StepExportErrors);
            if (extensionWarning != 0) Assert.Contains($"save_as_warnings: {extensionWarning}", diagnostics.StepExportWarnings);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FamilyStepExportStopsWhenClearingSelectionThrows()
    {
        var root = TempRoot("step-clear-selection-error");
        try
        {
            var com = new JacketComFacade(stepClearSelectionThrows: true);
            var builder = new JacketFeatureBuilder(
                com,
                planeSelector: new AlwaysSelectedPlaneSelector(),
                geometryReader: new FixedGeometryReader(ValidJacketGeometry()));

            var result = await builder.BuildAsync(JacketContext(root));

            Assert.Equal("Failed", result.Status);
            Assert.Contains(result.Issues, issue => issue.Contains("clear_selection_failed", StringComparison.Ordinal));
            Assert.Empty(com.StepSaveMethods);
            Assert.False(File.Exists(Path.ChangeExtension(com.SavedPartPath, ".STEP")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyPlateStepExportStopsWhenClearingSelectionThrows()
    {
        var root = TempRoot("plate-step-clear-selection-error");
        try
        {
            var com = new JacketComFacade(stepClearSelectionThrows: true);
            var partPath = Path.Combine(root, "jacket_basic.SLDPRT");
            var stepPath = Path.ChangeExtension(partPath, ".STEP");
            Assert.True(com.TryExtensionSaveAs(com.OriginalModel, partPath, null, [], []));
            var diagnostics = new SolidWorksPlateBuildDiagnostics { SldprtPath = partPath };
            var builder = new LateBoundSolidWorksPlateBuilder(com);
            var export = typeof(LateBoundSolidWorksPlateBuilder).GetMethod(
                "ExportStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                export.Invoke(builder, [new object(), com.OriginalModel, stepPath, diagnostics, new List<string>()]));

            Assert.IsType<System.Runtime.InteropServices.COMException>(exception.InnerException);
            Assert.False(diagnostics.StepExportSuccess);
            Assert.Empty(com.StepSaveMethods);
            Assert.False(File.Exists(stepPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerRejectsJacketBeforeComUntilProductionEvidenceIsActive()
    {
        var root = TempRoot("jacket-evidence-closed");
        Directory.CreateDirectory(root);
        var template = Path.Combine(root, "part.prtdot");
        await File.WriteAllTextAsync(template, "template");
        try
        {
            var state = new CoordinatedSessionState();
            var worker = Worker(
                state.CreateManager(),
                root,
                template,
                new PartFamilyBuilderRegistry([new JacketFeatureBuilder()]));

            var result = await worker.ExecuteAsync(RealRequest(JacketPlan(), root));

            Assert.Equal("Rejected", result.Status);
            Assert.Equal(PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient, result.FailureStage);
            Assert.False(result.RealCadConnected);
            Assert.False(result.RealCadExecuted);
            Assert.Equal(0, state.MaxConcurrentSessions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.GenericFeatureGraph);
            var registry = new PartFamilyBuilderRegistry([builder]);
            var session = new CoordinatedSessionState().CreateManager();
            var worker = Worker(session, root, template, registry);
            var result = await worker.ExecuteAsync(RealRequest(FlangePlan(), root));

            Assert.Equal("Completed", result.Status);
            Assert.Equal(1, builder.BuildCount);
            Assert.Equal(PartFamilyExecutionModes.GenericFeatureGraph, result.ExecutionMode);
            Assert.True(result.RealCadExecuted);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RealWorkerDoesNotGuessOwnershipOfPartDocumentsOrExitUserSession()
    {
        var root = TempRoot("controlled-document-cleanup");
        Directory.CreateDirectory(root);
        var template = Path.Combine(root, "part.prtdot");
        await File.WriteAllTextAsync(template, "template");
        try
        {
            var session = new DocumentTrackingSessionManager();
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.GenericFeatureGraph);
            var worker = Worker(session, root, template, new PartFamilyBuilderRegistry([builder]));

            var result = await worker.ExecuteAsync(RealRequest(FlangePlan(), root));

            Assert.Equal("Completed", result.Status);
            // 此替身没有创建文档。旧断言要求 Worker 猜名关闭，可能误关用户的同名零件。
            Assert.Empty(session.Application.ClosedDocumentNames);
            Assert.Equal(0, session.Application.ExitAppInvocations);
            Assert.DoesNotContain(result.Logs, log =>
                string.Equals(log, "solidworks_document_closed: flange_basic.SLDPRT", StringComparison.Ordinal));
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
            var builderOne = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.GenericFeatureGraph, 100);
            var builderTwo = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.GenericFeatureGraph, 100);
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
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.GenericFeatureGraph);
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
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.GenericFeatureGraph);
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
            var builder = new RecordingFamilyBuilder(FlangeBasicDefinition.Type, PartFamilyExecutionModes.GenericFeatureGraph);
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
            var step = Write(root, "flange_basic.STEP", MinimalStepContent);
            var reportPath = Path.Combine(root, "build_report.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                part_type = FlangeBasicDefinition.Type,
                execution_mode = PartFamilyExecutionModes.GenericFeatureGraph,
                execution_strategy = SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                solidworks_version = FeatureExecutionReportFixture.RuntimeVersion(),
                real_cad_executed = true,
                real_cad_connected = true,
                sldprt_save_success = true,
                step_export_success = true,
                final_status = "Passed",
                feature_handler_reports = FeatureExecutionReportFixture.FeatureResults(
                    FeatureTypes.ExtrudeBoss,
                    FeatureTypes.ExtrudeCut)
            }));
            var featureReport = FeatureExecutionReportFixture.Write(
                root,
                FeatureTypes.ExtrudeBoss,
                FeatureTypes.ExtrudeCut);
            var result = new SolidWorksWorkerResult(
                "validator",
                "Completed",
                [
                    Artifact(part, ".SLDPRT"),
                    Artifact(step, ".STEP"),
                    Artifact(reportPath, ".json"),
                    Artifact(featureReport, ".json")
                ],
                [],
                [],
                PartFamilyExecutionModes.GenericFeatureGraph,
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

    [Fact]
    public void ArtifactValidatorRejectsNonStepContentWithStepExtension()
    {
        var root = Path.Combine(FindProjectRoot(), "output", "solidworks", "real", $"v19-invalid-step-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var part = Write(root, "flange_basic.SLDPRT", "part");
            var step = Write(root, "flange_basic.STEP", "native SolidWorks bytes");
            var report = Write(root, "build_report.json", JsonSerializer.Serialize(new
            {
                part_type = FlangeBasicDefinition.Type,
                execution_mode = PartFamilyExecutionModes.GenericFeatureGraph,
                execution_strategy = SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                solidworks_version = FeatureExecutionReportFixture.RuntimeVersion(),
                real_cad_executed = true,
                real_cad_connected = true,
                sldprt_save_success = true,
                step_export_success = true,
                final_status = "Passed",
                feature_handler_reports = FeatureExecutionReportFixture.FeatureResults(
                    FeatureTypes.ExtrudeBoss,
                    FeatureTypes.ExtrudeCut)
            }));
            var result = new SolidWorksWorkerResult(
                "validator-invalid-step",
                "Completed",
                [Artifact(part, ".SLDPRT"), Artifact(step, ".STEP"), Artifact(report, ".json")],
                [],
                [],
                PartFamilyExecutionModes.GenericFeatureGraph,
                true,
                true);

            var validation = new SolidWorksArtifactValidator().Validate(result);

            Assert.False(validation.IsPassed);
            Assert.Contains(validation.Issues, issue =>
                issue.Contains("not valid STEP", StringComparison.OrdinalIgnoreCase));
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

    private static SolidWorksBuildPlan JacketPlan() =>
        new JacketBasicDefinition().GenerateBuildPlan("test-jacket", JacketSpec()).BuildPlan!;

    private static CADModelSpec JacketSpec() => new(
        "jacket",
        JacketBasicDefinition.Type,
        new Dictionary<string, string>
        {
            ["outer_diameter_mm"] = "140",
            ["inner_diameter_mm"] = "120",
            ["length_mm"] = "180"
        });

    private static PartFamilyBuildContext JacketContext(string output)
    {
        var options = new SolidWorksRuntimeOptions(
            true,
            false,
            Path.Combine(output, "part.prtdot"),
            output,
            2,
            5,
            MainWorkflowExecutionEnabled: true);
        return new PartFamilyBuildContext(
            new object(),
            RealRequest(JacketPlan(), output),
            options,
            "31.5.0");
    }

    private static MeasuredGeometry ValidJacketGeometry()
    {
        var volume = Math.PI / 4d * (140d * 140d - 120d * 120d) * 180d;
        return new MeasuredGeometry(
            RebuildPassed: true,
            BoundingBox: new GeometryBoundingBox(0d, 0d, 0d, 140d, 140d, 180d),
            ExactExtents: new GeometryBoundingBox(0d, 0d, 0d, 140d, 140d, 180d),
            BodyCount: 1,
            VolumeCubicMillimeters: volume,
            MassKilograms: null,
            MassPropertyVolumeCubicMillimeters: volume,
            CylindricalDiametersMm: [140d, 120d],
            ReadIssues: []);
    }

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

    private static void AssertFailedBuildReport(PartFamilyBuildResult result, string expectedFailureStage)
    {
        var reportArtifact = Assert.Single(result.Artifacts, artifact =>
            artifact.ArtifactType.Equals("BuildReport", StringComparison.OrdinalIgnoreCase));
        using var report = JsonDocument.Parse(File.ReadAllText(reportArtifact.FilePath));
        Assert.Equal("Failed", report.RootElement.GetProperty("final_status").GetString());
        Assert.Equal(expectedFailureStage, report.RootElement.GetProperty("failure_stage").GetString());
    }

    private const string MinimalStepContent =
        "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n";

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

    private sealed class FixedGeometryReader : ISolidWorksGeometryReader
    {
        private readonly MeasuredGeometry _geometry;

        public FixedGeometryReader(MeasuredGeometry geometry)
        {
            _geometry = geometry;
        }

        public SolidWorksGeometryReadResult Read(
            object model,
            CancellationToken cancellationToken = default) =>
            new(_geometry, null, []);
    }

    private sealed class AlwaysSelectedPlaneSelector : ISolidWorksPartFamilyPlaneSelector
    {
        public SolidWorksPlaneSelectionResult Select(object model) =>
            new(true, "Top Plane", "test", [], []);
    }

    private sealed class JacketComFacade : ISolidWorksComFacade
    {
        private readonly object _model = new();
        private readonly object _otherModel = new();
        private readonly object _unknownModel = new();
        private readonly object _sketchManager = new();
        private readonly object _featureManager = new();
        private readonly bool _rebuildResult;
        private readonly string? _failAt;
        private readonly string _stepContent;
        private readonly string _identityScenario;
        private readonly bool _stepExportResult;
        private readonly int _stepExportError;
        private readonly int _stepExportWarning;
        private readonly bool _stepClearSelectionThrows;
        private readonly bool _closeDocThrows;
        private object? _activeModel;
        private int _circleInvocationCount;

        public JacketComFacade(
            bool rebuildResult = true,
            string? failAt = null,
            string stepContent = MinimalStepContent,
            string identityScenario = "current-target",
            bool stepExportResult = true,
            int stepExportError = 0,
            int stepExportWarning = 0,
            bool stepClearSelectionThrows = false,
            bool closeDocThrows = false)
        {
            _rebuildResult = rebuildResult;
            _failAt = failAt;
            _stepContent = stepContent;
            _identityScenario = identityScenario;
            _stepExportResult = stepExportResult;
            _stepExportError = stepExportError;
            _stepExportWarning = stepExportWarning;
            _stepClearSelectionThrows = stepClearSelectionThrows;
            _closeDocThrows = closeDocThrows;
            _activeModel = identityScenario is "current-target" or "current-target-activation-wrong" ? _model
                : identityScenario == "initial-active-null" ? null : _otherModel;
        }

        public object OriginalModel => _model;
        public string? SavedPartPath { get; private set; }
        public int ActivationCount { get; private set; }
        public object?[]? ActivationArguments { get; private set; }
        public List<object> StepExportTargets { get; } = [];
        public List<string> StepSaveMethods { get; } = [];
        public List<string> ClosedDocumentNames { get; } = [];

        public object GetProperty(object target, string name) =>
            name switch
            {
                "SketchManager" => _sketchManager,
                "FeatureManager" => _featureManager,
                _ => new object()
            };

        public object? TryGetProperty(object? target, string name) =>
            name.Equals("ActiveDoc", StringComparison.OrdinalIgnoreCase) ? _activeModel : null;

        public object? TryGetIndexedProperty(object target, string name, params object?[] args) => null;

        public object? Invoke(object target, string name, params object?[] args) =>
            InvokeWithArgs(target, name, args);

        public object? InvokeWithArgs(object target, string name, object?[] args)
        {
            if (name == "CloseDoc")
            {
                if (_closeDocThrows) throw new System.Runtime.InteropServices.COMException("close_document_failed");
                ClosedDocumentNames.Add((string)args[0]!);
                return null;
            }
            if (name == "ClearSelection2" && _stepClearSelectionThrows && SavedPartPath is not null)
            {
                throw new System.Runtime.InteropServices.COMException("clear_selection_failed");
            }
            if (name == "GetPathName")
            {
                if (ReferenceEquals(target, _unknownModel)) return null;
                if (ReferenceEquals(target, _otherModel)) return OtherPartPath();
                return _identityScenario switch
                {
                    "missing-original-path" => null,
                    "relative-original-path" => Path.GetFileName(SavedPartPath),
                    "wrong-original-path" => OtherPartPath(),
                    _ => SavedPartPath
                };
            }
            if (name == "ActivateDoc3")
            {
                ActivationCount++;
                // 模拟 COM 的 out errors 和返回 ModelDoc2；文档标题相同不表示身份相同。
                if (_identityScenario != "activation-error-unwritten")
                {
                    args[3] = _identityScenario switch
                    {
                        "activation-error" => 1,
                        "activation-rebuild-warning" => 2,
                        _ => 0
                    };
                }
                ActivationArguments = args.ToArray();
                _activeModel = _identityScenario switch
                {
                    "same-title-wrong-document" or "active-wrong-document" or "current-target-activation-wrong" => _otherModel,
                    "active-null" => null,
                    _ => _model
                };
                return _identityScenario switch
                {
                    "same-title-wrong-document" or "returned-wrong-document" or "current-target-activation-wrong" => _otherModel,
                    "returned-null" => null,
                    "missing-returned-path" => _unknownModel,
                    _ => _model
                };
            }
            if ((name is "SaveAs3" or "SaveAs") && args[0] is string path &&
                path.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase))
            {
                StepExportTargets.Add(target);
                StepSaveMethods.Add(name);
                // 若生产回退到其他保存 API，故意返回成功；回归必须能发现这种掩盖。
                return true;
            }
            if (name.Equals("CreateCircle", StringComparison.OrdinalIgnoreCase))
            {
                _circleInvocationCount++;
                if (string.Equals(_failAt, $"CreateCircle:{_circleInvocationCount}", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            if (string.Equals(_failAt, name, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return name switch
            {
                "NewDocument" => _model,
                "GetTitle" => SavedPartPath is null ? "Part_test" : Path.GetFileName(SavedPartPath),
                "InsertSketch" => null,
                "CreateCircle" => new object(),
                "FeatureExtrusion2" => new object(),
                "FeatureCut4" => new object(),
                "ClearSelection2" => true,
                _ => new object()
            };
        }

        public object? TryInvoke(object? target, string name, params object?[] args)
        {
            try
            {
                return target is null ? null : Invoke(target, name, args);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        public object? TryInvokeWithArgs(object? target, string name, object?[] args) =>
            target is null ? null : InvokeWithArgs(target, name, args);

        public bool TryInvokeBool(object? target, string name, params object?[] args) =>
            name.Equals("ForceRebuild3", StringComparison.OrdinalIgnoreCase)
                ? _rebuildResult
                : TryInvoke(target, name, args) is bool value && value;

        public bool TrySetProperty(object target, string name, object? value) => true;

        public bool TryExtensionSaveAs(
            object model,
            string path,
            object? exportData,
            List<string> errors,
            List<string> warnings)
        {
            if (path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase))
            {
                SavedPartPath = Path.GetFullPath(path);
            }
            else if (path.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase))
            {
                StepExportTargets.Add(model);
                StepSaveMethods.Add("Extension.SaveAs");
                if (_stepExportError != 0) errors.Add($"save_as_errors: {_stepExportError}");
                if (_stepExportWarning != 0) warnings.Add($"save_as_warnings: {_stepExportWarning}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(
                path,
                path.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase)
                    ? _stepContent
                    : "controlled SolidWorks part bytes");
            return !path.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase) || _stepExportResult;
        }

        private string? OtherPartPath() => SavedPartPath is null ? null
            : Path.Combine(Path.GetDirectoryName(SavedPartPath)!, "previous", Path.GetFileName(SavedPartPath));

        public void ReleaseComObject(object value)
        {
        }
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

    private sealed class DocumentTrackingSessionManager : ISolidWorksSessionManager
    {
        public DocumentTrackingApplication Application { get; } = new();

        public Task<SolidWorksSessionConnectionResult> ConnectAsync(
            SolidWorksRuntimeOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SolidWorksSessionConnectionResult(true, "TestVersion", [], []));

        public Task<T> ExecuteWithApplicationAsync<T>(
            Func<object, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default,
            int? executionTimeoutSeconds = null) =>
            action(Application, cancellationToken);

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DocumentTrackingApplication
    {
        public List<string> ClosedDocumentNames { get; } = [];

        public int ExitAppInvocations { get; private set; }

        public void CloseDoc(string name) => ClosedDocumentNames.Add(name);

        public void ExitApp() => ExitAppInvocations++;
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
