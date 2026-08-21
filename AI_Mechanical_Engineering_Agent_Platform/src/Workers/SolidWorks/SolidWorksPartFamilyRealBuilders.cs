using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker;

public static class SolidWorksPartFamilyBuildModes
{
    public const string PlateBasic4Holes = PartFamilyExecutionModes.PlateBasic4Holes;
    public const string FlangeBasic = PartFamilyExecutionModes.FlangeBasic;
    public const string ShaftBasic = PartFamilyExecutionModes.ShaftBasic;

}

public sealed class SolidWorksPartFamilyBuildDiagnostics
{
    public List<string> OperationsExecuted { get; } = [];
    public List<Features.FeatureHandlerReport> FeatureHandlerReports { get; } = [];
    public List<string> Issues { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool RealCadExecuted { get; set; }
    public bool SldprtSaveAttempted { get; set; }
    public bool SldprtSaveSuccess { get; set; }
    public string? SldprtPath { get; set; }
    public long SldprtSizeBytes { get; set; }
    public bool StepExportAttempted { get; set; }
    public bool StepExportSuccess { get; set; }
    public bool StepContentValidated { get; set; }
    public string? StepPath { get; set; }
    public long StepSizeBytes { get; set; }
    public bool GeometryValidationAttempted { get; set; }
    public string? GeometryValidationStatus { get; set; }
    public int? MeasuredBodyCount { get; set; }
    public double? ExpectedVolumeCubicMillimeters { get; set; }
    public double? MeasuredVolumeCubicMillimeters { get; set; }
    public string? FailureStage { get; set; }
}

public static class SolidWorksPartFamilyBuildOutput
{
    public static string ResolveOutputDirectory(
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string partType)
    {
        var requestedRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? options.OutputDirectory
            : request.OutputDirectory;
        var root = Path.GetFullPath(requestedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = new DirectoryInfo(root);
        while (current is not null)
        {
            if (current.Name.Equals(partType, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            current = current.Parent;
        }

        return Path.Combine(root, partType, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
    }

    public static SolidWorksArtifact Artifact(
        string artifactId,
        string artifactType,
        string path,
        string expectedExtension,
        string description)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        return new SolidWorksArtifact(
            artifactId,
            artifactType,
            fullPath,
            expectedExtension,
            info.Exists,
            info.Exists ? info.Length : 0,
            description);
    }
}

public static class SolidWorksPartFamilyBuildReportWriter
{
    public static void Write(
        string reportPath,
        PartFamilyBuildContext context,
        IPartFamilyBuilder builder,
        string outputDirectory,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        string finalStatus)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report = new
        {
            build_id = $"solidworks-real-build-{Guid.NewGuid():N}",
            build_plan_id = context.Request.BuildPlan.PlanId,
            part_type = builder.PartType,
            execution_strategy = context.Request.BuildPlan.ExecutionStrategy,
            mode = "real",
            execution_mode = builder.RealExecutionMode,
            real_execution_requested = !context.Request.DryRun,
            real_cad_executed = diagnostics.RealCadExecuted,
            real_cad_connected = context.RealCadConnected,
            solidworks_version = context.SolidWorksVersion,
            output_directory = Path.GetFullPath(outputDirectory),
            sldprt_save_attempted = diagnostics.SldprtSaveAttempted,
            sldprt_save_success = diagnostics.SldprtSaveSuccess,
            sldprt_path = diagnostics.SldprtPath,
            sldprt_size_bytes = diagnostics.SldprtSizeBytes,
            step_export_attempted = diagnostics.StepExportAttempted,
            step_export_success = diagnostics.StepExportSuccess,
            step_content_validated = diagnostics.StepContentValidated,
            step_path = diagnostics.StepPath,
            step_size_bytes = diagnostics.StepSizeBytes,
            geometry_validation_attempted = diagnostics.GeometryValidationAttempted,
            geometry_validation_status = diagnostics.GeometryValidationStatus,
            measured_body_count = diagnostics.MeasuredBodyCount,
            expected_volume_cubic_mm = diagnostics.ExpectedVolumeCubicMillimeters,
            measured_volume_cubic_mm = diagnostics.MeasuredVolumeCubicMillimeters,
            failure_stage = diagnostics.FailureStage,
            final_status = finalStatus,
            api_evidence = builder.ApiEvidence,
            operations_executed = diagnostics.OperationsExecuted,
            feature_handler_reports = diagnostics.FeatureHandlerReports,
            feature_execution_report_path = diagnostics.FeatureHandlerReports.Count > 0
                ? Path.Combine(outputDirectory, "feature_execution_report.json")
                : null,
            issues = diagnostics.Issues,
            warnings = diagnostics.Warnings,
            generated_at = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                }));
    }
}

public static class SolidWorksFeatureExecutionReportWriter
{
    public static void Write(
        string reportPath,
        PartFamilyBuildContext context,
        IPartFamilyBuilder builder,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        string requestedFinalStatus)
    {
        var expectedFeatureCount = context.Request.BuildPlan.Operations.Count(operation =>
            !operation.OperationType.Equals("SavePart", StringComparison.OrdinalIgnoreCase) &&
            !operation.OperationType.Equals("ExportStep", StringComparison.OrdinalIgnoreCase));
        var allFeaturesExecuted =
            expectedFeatureCount > 0 &&
            diagnostics.FeatureHandlerReports.Count == expectedFeatureCount &&
            diagnostics.FeatureHandlerReports.All(report =>
                string.IsNullOrWhiteSpace(report.FailureStage) &&
                report.Issues.Count == 0);
        var allResultsValidated =
            allFeaturesExecuted &&
            diagnostics.FeatureHandlerReports.All(report => report.ResultObjectValidated);
        var allRebuildsPassed =
            allFeaturesExecuted &&
            diagnostics.FeatureHandlerReports.All(report => report.RebuildPassed);
        var allGeometryChangesValidated =
            allFeaturesExecuted &&
            diagnostics.FeatureHandlerReports.All(report => report.GeometryChangeValidated);
        var artifactsValidated =
            diagnostics.SldprtSaveSuccess &&
            diagnostics.SldprtSizeBytes > 0 &&
            diagnostics.StepExportSuccess &&
            diagnostics.StepContentValidated &&
            diagnostics.StepSizeBytes > 0;
        var passed =
            requestedFinalStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase) &&
            allResultsValidated &&
            allRebuildsPassed &&
            allGeometryChangesValidated &&
            artifactsValidated;
        var failureStage = passed
            ? null
            : diagnostics.FailureStage ??
              (!allFeaturesExecuted || !allResultsValidated || !allRebuildsPassed || !allGeometryChangesValidated
                  ? PartFamilyFailureStages.FeatureResultInvalid
                  : PartFamilyFailureStages.FeatureArtifactMissing);
        var report = new
        {
            report_id = $"solidworks-feature-execution-{Guid.NewGuid():N}",
            build_plan_id = context.Request.BuildPlan.PlanId,
            model_id = context.Request.BuildPlan.SourceCadModelSpecId,
            part_type = builder.PartType,
            execution_strategy = context.Request.BuildPlan.ExecutionStrategy,
            execution_mode = builder.RealExecutionMode,
            real_cad_executed = diagnostics.RealCadExecuted,
            real_cad_connected = context.RealCadConnected,
            solidworks_version = context.SolidWorksVersion,
            expected_feature_count = expectedFeatureCount,
            executed_feature_count = diagnostics.FeatureHandlerReports.Count,
            all_features_executed = allFeaturesExecuted,
            all_result_objects_validated = allResultsValidated,
            all_rebuilds_passed = allRebuildsPassed,
            all_geometry_changes_validated = allGeometryChangesValidated,
            artifacts_validated = artifactsValidated,
            sldprt_path = diagnostics.SldprtPath,
            sldprt_size_bytes = diagnostics.SldprtSizeBytes,
            step_path = diagnostics.StepPath,
            step_size_bytes = diagnostics.StepSizeBytes,
            step_content_validated = diagnostics.StepContentValidated,
            feature_results = diagnostics.FeatureHandlerReports,
            failure_stage = failureStage,
            final_status = passed ? "Passed" : "Failed",
            generated_at = DateTimeOffset.UtcNow
        };
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                }));
    }
}

public interface ISolidWorksPartFamilyPlaneSelector
{
    SolidWorksPlaneSelectionResult Select(object model);
}

public sealed class SolidWorksPartFamilyPlaneSelector : ISolidWorksPartFamilyPlaneSelector
{
    public SolidWorksPlaneSelectionResult Select(object model) =>
        new SolidWorksPlaneSelector().SelectStandardPlane(model);
}

public sealed class FlangeFeatureBuilder : SolidWorksPartFamilyBuilderBase
{
    private const double MmToMeters = 0.001;

    public FlangeFeatureBuilder(
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null,
        ISolidWorksPartFamilyPlaneSelector? planeSelector = null)
        : base(comFacade, fileVerifier, planeSelector)
    {
    }

    public override string PartType => FlangeBasicDefinition.Type;
    public override string FailureStage => PartFamilyFailureStages.FlangeBuildFailed;
    public override bool SupportsRealExecution => true;
    public override string ApiEvidence =>
        "CreateCircle_FeatureExtrusion2_and_FeatureCut4; v1_9_flange_diagnostic_visual_review_and_main_workflow_passed";
    public override string RealExecutionMode => SolidWorksPartFamilyBuildModes.FlangeBasic;

    protected override void BuildFeatures(
        object model,
        SolidWorksBuildPlan plan,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        List<string> logs)
    {
        var dimensions = plan.Dimensions ?? throw Failure(
            PartFamilyFailureStages.FlangeProfileCreateFailed,
            "flange dimensions are missing from BuildPlan.");
        var outerDiameter = RequiredPositive(dimensions, "outer_diameter_mm", PartFamilyFailureStages.FlangeProfileCreateFailed);
        var innerDiameter = RequiredPositive(dimensions, "inner_diameter_mm", PartFamilyFailureStages.FlangeInnerCutFailed);
        var thickness = RequiredPositive(dimensions, "thickness_mm", PartFamilyFailureStages.FlangeExtrudeFailed);
        var boltHoleDiameter = RequiredPositive(dimensions, "bolt_hole_diameter_mm", PartFamilyFailureStages.FlangeBoltHolesFailed);
        var boltCircleDiameter = RequiredPositive(dimensions, "bolt_circle_diameter_mm", PartFamilyFailureStages.FlangeBoltHolesFailed);
        var boltHoleCount = RequiredInteger(dimensions, "bolt_hole_count", PartFamilyFailureStages.FlangeBoltHolesFailed);

        diagnostics.OperationsExecuted.Add("flange_profile_create_started");
        SelectPlane(model, PartFamilyFailureStages.FlangeProfileCreateFailed);
        var sketchManager = Com.GetProperty(model, "SketchManager");
        Com.Invoke(sketchManager, "InsertSketch", true);
        Require(
            Com.Invoke(sketchManager, "CreateCircle", 0d, 0d, 0d, outerDiameter * MmToMeters / 2d, 0d, 0d),
            PartFamilyFailureStages.FlangeProfileCreateFailed,
            "CreateCircle returned null for the outer profile.");
        diagnostics.OperationsExecuted.Add("flange_profile_create_success");

        diagnostics.OperationsExecuted.Add("flange_extrude_started");
        var featureManager = Com.GetProperty(model, "FeatureManager");
        Require(
            Com.InvokeWithArgs(featureManager, "FeatureExtrusion2", FeatureExtrusion2Arguments(thickness * MmToMeters)),
            PartFamilyFailureStages.FlangeExtrudeFailed,
            "FeatureExtrusion2 returned null.");
        diagnostics.OperationsExecuted.Add("flange_extrude_success");

        diagnostics.OperationsExecuted.Add("flange_inner_cut_started");
        SelectPlane(model, PartFamilyFailureStages.FlangeInnerCutFailed);
        sketchManager = Com.GetProperty(model, "SketchManager");
        Com.Invoke(sketchManager, "InsertSketch", true);
        Require(
            Com.Invoke(sketchManager, "CreateCircle", 0d, 0d, 0d, innerDiameter * MmToMeters / 2d, 0d, 0d),
            PartFamilyFailureStages.FlangeInnerCutFailed,
            "CreateCircle returned null for the centre hole.");
        Require(
            Com.InvokeWithArgs(featureManager, "FeatureCut4", ActiveSketchFeatureCut4Arguments(thickness * MmToMeters * 2d)),
            PartFamilyFailureStages.FlangeInnerCutFailed,
            "FeatureCut4 returned null for the centre hole.");
        diagnostics.OperationsExecuted.Add("flange_inner_cut_success");

        diagnostics.OperationsExecuted.Add("flange_bolt_holes_started");
        SelectPlane(model, PartFamilyFailureStages.FlangeBoltHolesFailed);
        sketchManager = Com.GetProperty(model, "SketchManager");
        Com.Invoke(sketchManager, "InsertSketch", true);
        var boltRadius = boltCircleDiameter * MmToMeters / 2d;
        var holeRadius = boltHoleDiameter * MmToMeters / 2d;
        for (var index = 0; index < boltHoleCount; index++)
        {
            var angle = 2d * Math.PI * index / boltHoleCount;
            var x = boltRadius * Math.Cos(angle);
            var y = boltRadius * Math.Sin(angle);
            Require(
                Com.Invoke(sketchManager, "CreateCircle", x, y, 0d, x + holeRadius, y, 0d),
                PartFamilyFailureStages.FlangeBoltHolesFailed,
                $"CreateCircle returned null for bolt hole {index + 1}.");
        }

        Require(
            Com.InvokeWithArgs(featureManager, "FeatureCut4", ActiveSketchFeatureCut4Arguments(thickness * MmToMeters * 2d)),
            PartFamilyFailureStages.FlangeBoltHolesFailed,
            "FeatureCut4 returned null for the bolt holes.");
        diagnostics.OperationsExecuted.Add("flange_bolt_holes_success");
        logs.Add($"flange_direct_bolt_hole_centres_created: {boltHoleCount}");
    }

    public static object?[] FeatureExtrusion2Arguments(double depthMeters) =>
    [
        true, false, false, 0, 0, depthMeters, 0d, false, false, false, false,
        0d, 0d, false, false, false, false, true, true, true, 0, 0d, false
    ];

    public static object?[] ActiveSketchFeatureCut4Arguments(double cutDepthMeters)
    {
        const int swEndCondBlind = 0;
        var oneDegreeRadians = Math.PI / 180d;
        return
        [
            true, false, true,
            swEndCondBlind, swEndCondBlind,
            cutDepthMeters, cutDepthMeters,
            false, false, false, false,
            oneDegreeRadians, oneDegreeRadians,
            false, false, false, false,
            false, true, true,
            true, false, false,
            0, 0d, false, false
        ];
    }
}

public sealed class ShaftFeatureBuilder : SolidWorksPartFamilyBuilderBase
{
    private const double MmToMeters = 0.001;

    public ShaftFeatureBuilder(
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null,
        ISolidWorksPartFamilyPlaneSelector? planeSelector = null)
        : base(comFacade, fileVerifier, planeSelector)
    {
    }

    public override string PartType => ShaftBasicDefinition.Type;
    public override string FailureStage => PartFamilyFailureStages.ShaftBuildFailed;
    public override bool SupportsRealExecution => true;
    public override string ApiEvidence =>
        "CreateLine_CreateCenterLine_Select4_marks_and_exact_20_argument_FeatureRevolve2; v1_9_shaft_diagnostic_visual_review_and_main_workflow_passed";
    public override string RealExecutionMode => SolidWorksPartFamilyBuildModes.ShaftBasic;

    protected override void BuildFeatures(
        object model,
        SolidWorksBuildPlan plan,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        List<string> logs)
    {
        var dimensions = plan.Dimensions ?? throw Failure(
            PartFamilyFailureStages.ShaftProfileCreateFailed,
            "shaft dimensions are missing from BuildPlan.");
        var baseDiameter = RequiredPositive(dimensions, "diameter_mm", PartFamilyFailureStages.ShaftProfileCreateFailed);
        var overallLength = RequiredPositive(dimensions, "length_mm", PartFamilyFailureStages.ShaftProfileCreateFailed);
        var stepDiameters = ParseList(dimensions.GetValueOrDefault("optional_step_diameters"));
        var stepLengths = ParseList(dimensions.GetValueOrDefault("optional_step_lengths"));
        if (stepDiameters.Count != stepLengths.Count || stepLengths.Sum() >= overallLength)
        {
            throw Failure(PartFamilyFailureStages.ShaftStepFeatureFailed, "optional shaft step mappings are inconsistent.");
        }

        diagnostics.OperationsExecuted.Add("shaft_profile_create_started");
        SelectPlane(model, PartFamilyFailureStages.ShaftProfileCreateFailed);
        var sketchManager = Com.GetProperty(model, "SketchManager");
        Com.Invoke(sketchManager, "InsertSketch", true);
        var segments = BuildSegments(baseDiameter, overallLength, stepDiameters, stepLengths);
        foreach (var segment in segments)
        {
            Require(
                Com.Invoke(
                    sketchManager,
                    "CreateLine",
                    segment.X1 * MmToMeters,
                    segment.Y1 * MmToMeters,
                    0d,
                    segment.X2 * MmToMeters,
                    segment.Y2 * MmToMeters,
                    0d),
                segment.IsStepTransition
                    ? PartFamilyFailureStages.ShaftStepFeatureFailed
                    : PartFamilyFailureStages.ShaftProfileCreateFailed,
                "CreateLine returned null while creating the shaft half-section.");
        }

        var centerLine = Require(
            Com.Invoke(sketchManager, "CreateCenterLine", 0d, 0d, 0d, overallLength * MmToMeters, 0d, 0d),
            PartFamilyFailureStages.ShaftProfileCreateFailed,
            "CreateCenterLine returned null.");
        var activeSketch = Com.TryInvoke(model, "GetActiveSketch2") ?? Com.TryGetProperty(sketchManager, "ActiveSketch");
        var profileFeature = Com.TryInvoke(activeSketch, "GetFeature") ?? activeSketch;
        Require(profileFeature, PartFamilyFailureStages.ShaftProfileCreateFailed, "active shaft profile sketch could not be captured.");
        Com.Invoke(sketchManager, "InsertSketch", true);
        diagnostics.OperationsExecuted.Add("shaft_profile_create_success");

        diagnostics.OperationsExecuted.Add("shaft_revolve_started");
        var selectionManager = Com.GetProperty(model, "SelectionManager");
        var profileSelectData = Require(
            Com.Invoke(selectionManager, "CreateSelectData"),
            PartFamilyFailureStages.ShaftRevolveFailed,
            "CreateSelectData returned null for the profile.");
        var axisSelectData = Require(
            Com.Invoke(selectionManager, "CreateSelectData"),
            PartFamilyFailureStages.ShaftRevolveFailed,
            "CreateSelectData returned null for the centreline.");
        if (!Com.TrySetProperty(profileSelectData, "Mark", 0) ||
            !Com.TrySetProperty(axisSelectData, "Mark", 16))
        {
            throw Failure(PartFamilyFailureStages.ShaftRevolveFailed, "ISelectData.Mark could not be assigned.");
        }

        if (!Com.TryInvokeBool(profileFeature, "Select4", false, profileSelectData) ||
            !Com.TryInvokeBool(centerLine, "Select4", true, axisSelectData))
        {
            throw Failure(PartFamilyFailureStages.ShaftRevolveFailed, "profile mark 0 or centreline mark 16 selection failed.");
        }

        var featureManager = Com.GetProperty(model, "FeatureManager");
        Require(
            Com.InvokeWithArgs(featureManager, "FeatureRevolve2", FeatureRevolve2Arguments()),
            PartFamilyFailureStages.ShaftRevolveFailed,
            "FeatureRevolve2 returned null.");
        diagnostics.OperationsExecuted.Add("shaft_revolve_success");
        logs.Add($"shaft_half_profile_segments_created: {segments.Count}");
    }

    public static object?[] FeatureRevolve2Arguments() =>
    [
        true, true, false, false, false, false, 0, 0,
        2d * Math.PI, 0d, false, false, 0.01d, 0.01d,
        0, 0, 0, true, true, true
    ];

    public static IReadOnlyList<ShaftProfileSegment> BuildSegments(
        double baseDiameterMm,
        double overallLengthMm,
        IReadOnlyList<double> stepDiametersMm,
        IReadOnlyList<double> stepLengthsMm)
    {
        if (stepDiametersMm.Count != stepLengthsMm.Count)
        {
            throw Failure(PartFamilyFailureStages.ShaftStepFeatureFailed, "optional shaft step list counts differ.");
        }

        var sectionDiameters = new List<double> { baseDiameterMm };
        sectionDiameters.AddRange(stepDiametersMm);
        var sectionLengths = new List<double> { overallLengthMm - stepLengthsMm.Sum() };
        sectionLengths.AddRange(stepLengthsMm);
        var segments = new List<ShaftProfileSegment>();
        var x = 0d;
        var radius = sectionDiameters[0] / 2d;
        segments.Add(new(0d, 0d, 0d, radius, false));
        for (var index = 0; index < sectionLengths.Count; index++)
        {
            var nextX = x + sectionLengths[index];
            segments.Add(new(x, radius, nextX, radius, false));
            x = nextX;
            if (index + 1 < sectionDiameters.Count)
            {
                var nextRadius = sectionDiameters[index + 1] / 2d;
                segments.Add(new(x, radius, x, nextRadius, true));
                radius = nextRadius;
            }
        }

        segments.Add(new(overallLengthMm, radius, overallLengthMm, 0d, false));
        segments.Add(new(overallLengthMm, 0d, 0d, 0d, false));
        return segments;
    }
}

public sealed record ShaftProfileSegment(
    double X1,
    double Y1,
    double X2,
    double Y2,
    bool IsStepTransition);

public abstract class SolidWorksPartFamilyBuilderBase : TextPlaceholderPartFamilyBuilder
{
    protected SolidWorksPartFamilyBuilderBase(
        ISolidWorksComFacade? comFacade,
        ISolidWorksFileVerifier? fileVerifier,
        ISolidWorksPartFamilyPlaneSelector? planeSelector)
    {
        Com = comFacade ?? new LateBoundSolidWorksComFacade();
        FileVerifier = fileVerifier ?? new SolidWorksFileVerifier();
        PlaneSelector = planeSelector ?? new SolidWorksPartFamilyPlaneSelector();
    }

    protected ISolidWorksComFacade Com { get; }
    protected ISolidWorksFileVerifier FileVerifier { get; }
    protected ISolidWorksPartFamilyPlaneSelector PlaneSelector { get; }
    /// <summary>
    /// Every real part-family build must end on a successful final rebuild.
    /// A family that cannot satisfy this has to relax it explicitly and state
    /// why, so a silently discarded rebuild result can never be introduced by
    /// simply adding a new builder.
    /// </summary>
    protected virtual bool RequiresStrictFinalRebuild => true;

    public override Task<PartFamilyBuildResult> BuildAsync(
        PartFamilyBuildContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.Request.BuildPlan.PartType, PartType, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new PartFamilyBuildResult(
                "Rejected", [], [], [$"{FailureStage}: builder for {PartType} cannot build {context.Request.BuildPlan.PartType}."],
                RealExecutionMode, false, FailureStage));
        }

        return Task.Run(() => BuildCore(context, cancellationToken), cancellationToken);
    }

    protected abstract void BuildFeatures(
        object model,
        SolidWorksBuildPlan plan,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        List<string> logs);

    protected void SelectPlane(object model, string failureStage)
    {
        var selection = PlaneSelector.Select(model);
        if (!selection.Success)
        {
            throw Failure(failureStage, selection.Errors.LastOrDefault() ?? "standard plane selection failed.");
        }
    }

    protected static object Require(object? result, string stage, string message) =>
        result ?? throw Failure(stage, message);

    protected static PartFamilyBuildException Failure(string stage, string message) => new(stage, message);

    protected static double RequiredPositive(IReadOnlyDictionary<string, string> values, string name, string stage)
    {
        if (!values.TryGetValue(name, out var text) ||
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value <= 0)
        {
            throw Failure(stage, $"{name} must be a finite positive number.");
        }

        return value;
    }

    protected static int RequiredInteger(IReadOnlyDictionary<string, string> values, string name, string stage)
    {
        if (!values.TryGetValue(name, out var text) ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw Failure(stage, $"{name} must be a positive integer.");
        }

        return value;
    }

    protected static IReadOnlyList<double> ParseList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<double>();
        }

        var values = new List<double>();
        foreach (var item in text.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value <= 0)
            {
                return Array.Empty<double>();
            }

            values.Add(value);
        }

        return values;
    }

    private PartFamilyBuildResult BuildCore(PartFamilyBuildContext context, CancellationToken cancellationToken)
    {
        var diagnostics = new SolidWorksPartFamilyBuildDiagnostics();
        var logs = new List<string>();
        var outputDirectory = SolidWorksPartFamilyBuildOutput.ResolveOutputDirectory(
            context.Request,
            context.Options,
            PartType);
        var partPath = Path.Combine(outputDirectory, $"{PartType}.SLDPRT");
        var stepPath = Path.Combine(outputDirectory, $"{PartType}.STEP");
        var reportPath = Path.Combine(outputDirectory, "build_report.json");
        var featureExecutionReportPath = Path.Combine(outputDirectory, "feature_execution_report.json");
        diagnostics.SldprtPath = partPath;
        diagnostics.StepPath = stepPath;
        object? model = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputDirectory);
            diagnostics.OperationsExecuted.Add("new_part_started");
            model = Require(
                Com.Invoke(context.Application, "NewDocument", context.Options.TemplatePartPath!, 0, 0d, 0d),
                FailureStage,
                "NewDocument returned null.");
            diagnostics.RealCadExecuted = true;
            diagnostics.OperationsExecuted.Add("new_part_success");
            BuildFeatures(model, context.Request.BuildPlan, diagnostics, logs);
            var finalRebuildPassed = Com.TryInvokeBool(model, "ForceRebuild3", false);
            if (RequiresStrictFinalRebuild && !finalRebuildPassed)
            {
                throw Failure(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    "Final ForceRebuild3(false) did not return true.");
            }
            Save(model, partPath, diagnostics);
            Export(context.Application, model, stepPath, diagnostics);
            SolidWorksPartFamilyBuildReportWriter.Write(reportPath, context, this, outputDirectory, diagnostics, "Passed");
            var artifacts = new List<SolidWorksArtifact>
            {
                    SolidWorksPartFamilyBuildOutput.Artifact("real-part", "Part", partPath, ".SLDPRT", $"Real {PartType} SolidWorks part."),
                    SolidWorksPartFamilyBuildOutput.Artifact("real-step", "Step", stepPath, ".STEP", $"Real {PartType} STEP export."),
                    SolidWorksPartFamilyBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "Real part-family build report.")
            };
            if (diagnostics.FeatureHandlerReports.Count > 0)
            {
                SolidWorksFeatureExecutionReportWriter.Write(
                    featureExecutionReportPath,
                    context,
                    this,
                    diagnostics,
                    "Passed");
                artifacts.Add(SolidWorksPartFamilyBuildOutput.Artifact(
                    "feature-execution-report",
                    "FeatureExecutionReport",
                    featureExecutionReportPath,
                    ".json",
                    "Per-feature SolidWorks execution evidence report."));
            }

            return new PartFamilyBuildResult(
                "Completed",
                artifacts,
                logs.Concat(diagnostics.OperationsExecuted.Select(operation => $"operation_executed: {operation}")).ToArray(),
                [],
                RealExecutionMode,
                RealCadExecuted: true);
        }
        catch (PartFamilyBuildException ex)
        {
            diagnostics.FailureStage = ex.Stage;
            diagnostics.Issues.Add($"{ex.Stage}: {ex.Message}");
            return FailedWithReport(
                context,
                outputDirectory,
                reportPath,
                featureExecutionReportPath,
                diagnostics,
                logs);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMethodException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            diagnostics.FailureStage = FailureStage;
            diagnostics.Issues.Add($"{FailureStage}: {ex.GetBaseException().Message}");
            return FailedWithReport(
                context,
                outputDirectory,
                reportPath,
                featureExecutionReportPath,
                diagnostics,
                logs);
        }
        finally
        {
            if (model is not null)
            {
                Com.ReleaseComObject(model);
            }
        }
    }

    private PartFamilyBuildResult FailedWithReport(
        PartFamilyBuildContext context,
        string outputDirectory,
        string reportPath,
        string featureExecutionReportPath,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        IReadOnlyList<string> logs)
    {
        var artifacts = new List<SolidWorksArtifact>();
        try
        {
            SolidWorksPartFamilyBuildReportWriter.Write(reportPath, context, this, outputDirectory, diagnostics, "Failed");
            artifacts.Add(SolidWorksPartFamilyBuildOutput.Artifact(
                "real-build-report", "BuildReport", reportPath, ".json", "Real part-family build failure report."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Issues.Add($"build_report_write_failed: {ex.Message}");
        }

        if (diagnostics.FeatureHandlerReports.Count > 0)
        {
            try
            {
                SolidWorksFeatureExecutionReportWriter.Write(
                    featureExecutionReportPath,
                    context,
                    this,
                    diagnostics,
                    "Failed");
                artifacts.Add(SolidWorksPartFamilyBuildOutput.Artifact(
                    "feature-execution-report",
                    "FeatureExecutionReport",
                    featureExecutionReportPath,
                    ".json",
                    "Per-feature SolidWorks execution failure report."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Issues.Add($"feature_execution_report_write_failed: {ex.Message}");
            }
        }

        return new PartFamilyBuildResult(
            "Failed",
            artifacts,
            logs.Concat(diagnostics.OperationsExecuted.Select(operation => $"operation_executed: {operation}")).ToArray(),
            diagnostics.Issues,
            RealExecutionMode,
            diagnostics.RealCadExecuted,
            diagnostics.FailureStage ?? FailureStage);
    }

    private void Save(object model, string path, SolidWorksPartFamilyBuildDiagnostics diagnostics)
    {
        diagnostics.SldprtSaveAttempted = true;
        diagnostics.OperationsExecuted.Add("part_save_started");
        var errors = new List<string>();
        var warnings = new List<string>();
        var saved = Com.TryExtensionSaveAs(model, path, null, errors, warnings) ||
                    Com.TryInvokeBool(model, "SaveAs3", path, 0, 1) ||
                    Com.TryInvokeBool(model, "SaveAs", path);
        if (!saved)
        {
            diagnostics.Issues.AddRange(errors);
            diagnostics.Warnings.AddRange(warnings);
            throw Failure(PartFamilyFailureStages.PartSaveFailed, "SLDPRT SaveAs returned false.");
        }

        var state = FileVerifier.GetState(path);
        if (!state.Exists || state.SizeBytes <= 0)
        {
            throw Failure(PartFamilyFailureStages.PartSaveFailed, "SLDPRT output is missing or empty.");
        }

        diagnostics.SldprtSaveSuccess = true;
        diagnostics.SldprtSizeBytes = state.SizeBytes;
        diagnostics.OperationsExecuted.Add("part_save_success");
    }

    private void Export(
        object application,
        object model,
        string path,
        SolidWorksPartFamilyBuildDiagnostics diagnostics)
    {
        diagnostics.StepExportAttempted = true;
        diagnostics.OperationsExecuted.Add("step_export_started");
        var title = Com.TryInvoke(model, "GetTitle")?.ToString();
        if (!string.IsNullOrWhiteSpace(title))
        {
            Com.TryInvoke(application, "ActivateDoc3", title, true, 0, 0);
        }

        var activeDocument = Com.TryGetProperty(application, "ActiveDoc") ?? model;
        Com.TryInvoke(activeDocument, "ClearSelection2", true);
        var errors = new List<string>();
        var warnings = new List<string>();
        var exported = Com.TryExtensionSaveAs(activeDocument, path, null, errors, warnings) ||
                       Com.TryInvokeBool(activeDocument, "SaveAs3", path, 0, 1) ||
                       Com.TryInvokeBool(activeDocument, "SaveAs", path);
        if (!exported)
        {
            diagnostics.Issues.AddRange(errors);
            diagnostics.Warnings.AddRange(warnings);
            throw Failure(PartFamilyFailureStages.StepExportFailed, "STEP SaveAs returned false.");
        }

        var state = FileVerifier.GetState(path);
        if (!state.Exists || state.SizeBytes <= 0)
        {
            throw Failure(PartFamilyFailureStages.StepExportFailed, "STEP output is missing or empty.");
        }

        if (!CadArtifactContentValidator.TryValidateStepFile(path, out var contentIssue))
        {
            throw Failure(
                PartFamilyFailureStages.StepExportFailed,
                $"STEP output content validation failed: {contentIssue}");
        }

        diagnostics.StepExportSuccess = true;
        diagnostics.StepContentValidated = true;
        diagnostics.StepSizeBytes = state.SizeBytes;
        diagnostics.OperationsExecuted.Add("step_export_success");
    }
}

public sealed class PartFamilyBuildException : Exception
{
    public PartFamilyBuildException(string stage, string message)
        : base(message)
    {
        Stage = stage;
    }

    public string Stage { get; }
}
