using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>
/// V2.0-D-only authorization for the four-hole plate's three-circle blind cut.
/// It supplements, and never broadens, the V2.0-C per-handler evidence: the
/// FeatureHandlerRegistry must already have passed before this policy is used.
/// This boundary is pure plan/file evidence validation and never accesses COM.
/// </summary>
public static partial class V20DThreeCircleCutEvidencePolicy
{
    public const string EvidenceId = "v2.0-d-20260803-three-circle-blind-cut";
    public const string ParameterProfile =
        "three_circles;diameter_10_mm;blind;single_end;positive_depth_mm;through_all_false;single_body_scope;plate_basic_4holes";
    public const string SolidWorksVersion = "31.5.0";
    public const string SampleInputRelativePath = "examples/parameter_update_plate.json";
    public const string CandidateDiagnosticRelativePath =
        "output/solidworks/features/20260803_064124_6127412/feature_execution_report.json";

    // These values deliberately bind the exact sample and candidate run. They
    // are normalized out of the source-revision calculation below so that a
    // refreshed diagnostic can update them without creating a circular hash.
    public const string SampleInputSha256 = "6B0B0E66610E1729479A538902D060EB874E0B6EC7FD6726EBA85E2D0D8C9732";
    public const string CandidateReportSha256 = "9DE2C3399C739017C661AF02AB58ADDC33D5AAB52BF93B7170624F606D203EDF";
    public const string SourceRevision = "v2.0-d-three-circle-source-sha256:d68c389da475c43e4860573e0543be64895ed381a69395d6c1142cd4ec59e3ba";

    private const string SourceRevisionPrefix = "v2.0-d-three-circle-source-sha256:";
    private const double Tolerance = 1e-6;
    private const double CubicMillimetersPerCubicMeter = 1_000_000_000d;
    private static readonly string[] BoundSourcePaths =
    [
        "src/Workers/SolidWorks/Features/V20DThreeCircleCutEvidencePolicy.cs",
        "src/Modules/CADModeling/ModelUpdateService.cs",
        "src/Workers/SolidWorks/RealSolidWorksWorker.cs",
        SampleInputRelativePath
    ];

    /// <summary>
    /// Complete V2.0-D evidence gate. It is called after the V2.0-C Handler
    /// gate and before SolidWorks is connected by RealSolidWorksWorker.
    /// </summary>
    public static V20DThreeCircleCutEvidenceValidationResult ValidateForRealExecution(
        SolidWorksBuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var issues = new List<string>();

        var profile = ValidatePlanProfile(plan);
        issues.AddRange(profile.Issues);

        try
        {
            var currentRevision = ComputeCurrentSourceRevision();
            if (!string.Equals(SourceRevision, currentRevision, StringComparison.Ordinal))
            {
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D three-circle source revision is stale; " +
                    $"expected {currentRevision}, evidence has {SourceRevision}.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D three-circle source revision cannot be computed: " +
                exception.Message);
        }

        ValidateSampleInputFingerprint(issues);
        ValidateCandidateDiagnostic(issues);
        return issues.Count == 0
            ? V20DThreeCircleCutEvidenceValidationResult.Passed()
            : V20DThreeCircleCutEvidenceValidationResult.Failed(issues);
    }

    /// <summary>
    /// Validates the safe parameter envelope independently of filesystem
    /// evidence. Kept public so unit tests can exercise the fail-closed graph
    /// binding without requiring a SolidWorks diagnostic artifact.
    /// </summary>
    public static V20DThreeCircleCutEvidenceValidationResult ValidatePlanProfile(
        SolidWorksBuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var issues = new List<string>();

        if (!plan.PartType.Equals(PlateBasic4HolesDefinition.Type, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"V2.0-D three-circle profile requires part_type={PlateBasic4HolesDefinition.Type}.");
        }

        if (!plan.ExecutionStrategy.Equals(
                SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("V2.0-D three-circle profile requires feature_handler_graph execution.");
        }

        if (!TryReadPositiveNumber(plan.Dimensions, "length_mm", out var length) ||
            !TryReadPositiveNumber(plan.Dimensions, "width_mm", out var width) ||
            !TryReadPositiveNumber(plan.Dimensions, "thickness_mm", out var thickness) ||
            !TryReadPositiveNumber(plan.Dimensions, "hole_diameter_mm", out var diameter) ||
            !TryReadPositiveNumber(plan.Dimensions, "hole_count", out var holeCount))
        {
            issues.Add("V2.0-D three-circle profile requires finite positive length_mm, width_mm, thickness_mm, hole_diameter_mm and hole_count.");
            return V20DThreeCircleCutEvidenceValidationResult.Failed(issues);
        }

        if (!IsSupportedPlateSize(length, width, thickness))
        {
            issues.Add("V2.0-D three-circle profile is authorized only for 160x80x12 or 200x100x15 mm plate states.");
        }

        if (!NearlyEqual(diameter, 10d) || !NearlyEqual(holeCount, 4d))
        {
            issues.Add("V2.0-D three-circle profile requires hole_count=4 and hole_diameter_mm=10.");
        }

        var geometryOperations = plan.Operations.Where(operation =>
            !operation.OperationType.Equals("SavePart", StringComparison.OrdinalIgnoreCase) &&
            !operation.OperationType.Equals("ExportStep", StringComparison.OrdinalIgnoreCase)).ToArray();
        var expectedOperationTypes = new[]
        {
            "CreateSketch",
            "ExtrudeBoss",
            "CreateSketch",
            "CutExtrude",
            "CreateSketch",
            "CreateSimpleHole"
        };
        if (!geometryOperations.Select(operation => operation.OperationType).SequenceEqual(
                expectedOperationTypes,
                StringComparer.OrdinalIgnoreCase))
        {
            issues.Add("V2.0-D three-circle profile requires exactly sketch, boss, sketch, cut, sketch and simple-hole operations in order.");
            return V20DThreeCircleCutEvidenceValidationResult.Failed(issues);
        }

        var plateSketch = geometryOperations[0];
        var boss = geometryOperations[1];
        var cutSketch = geometryOperations[2];
        var cut = geometryOperations[3];
        var holeSketch = geometryOperations[4];
        var hole = geometryOperations[5];

        ValidateSketchIdentity(plateSketch, "plate_profile", issues);
        ValidateSketchIdentity(cutSketch, "cut_profile", issues);
        ValidateSketchIdentity(holeSketch, "hole_profile", issues);
        ValidateFeatureIdentity(boss, "plate_boss", FeatureTypes.ExtrudeBoss, issues);
        ValidateFeatureIdentity(cut, "plate_cut", FeatureTypes.ExtrudeCut, issues);
        ValidateFeatureIdentity(hole, "plate_hole", FeatureTypes.Hole, issues);

        ValidatePlateRectangle(plateSketch, length, width, issues);
        ValidateCutCircles(cutSketch, length, width, diameter, issues);
        ValidateSingleHoleCircle(holeSketch, length, width, diameter, issues);
        ValidateBoss(boss, plateSketch, thickness, issues);
        ValidateCut(cut, cutSketch, boss, thickness, issues);
        ValidateHole(hole, holeSketch, cut, thickness, diameter, issues);

        return issues.Count == 0
            ? V20DThreeCircleCutEvidenceValidationResult.Passed()
            : V20DThreeCircleCutEvidenceValidationResult.Failed(issues);
    }

    public static string ComputeCurrentSourceRevision()
    {
        var root = FindProjectRoot();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relativePath in BoundSourcePaths.Order(StringComparer.Ordinal))
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"V2.0-D three-circle evidence source is missing: {relativePath}.", fullPath);
            }

            Append(hash, relativePath.Replace('\\', '/'));
            Append(hash, "\n");
            var content = relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes(NormalizeVolatileEvidenceMetadata(File.ReadAllText(fullPath)))
                : File.ReadAllBytes(fullPath);
            hash.AppendData(content);
            Append(hash, "\n");
        }

        return SourceRevisionPrefix + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void ValidateSampleInputFingerprint(List<string> issues)
    {
        try
        {
            var path = ResolveProjectPath(SampleInputRelativePath);
            var actual = ComputeFileSha256(path);
            if (!string.Equals(actual, SampleInputSha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D sample input hash is stale; " +
                    $"expected {actual}, evidence has {SampleInputSha256}.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D sample input cannot be hashed: {exception.Message}");
        }
    }

    private static void ValidateCandidateDiagnostic(List<string> issues)
    {
        string candidatePath;
        try
        {
            candidatePath = ResolveProjectPath(CandidateDiagnosticRelativePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or DirectoryNotFoundException)
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate diagnostic path is invalid: {exception.Message}");
            return;
        }

        if (!File.Exists(candidatePath))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate diagnostic does not exist: {candidatePath}.");
            return;
        }

        try
        {
            var actualHash = ComputeFileSha256(candidatePath);
            if (!string.Equals(actualHash, CandidateReportSha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate diagnostic hash is stale; " +
                    $"expected {actualHash}, evidence has {CandidateReportSha256}.");
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(candidatePath));
            var root = document.RootElement;
            RequireBoolean(root, "candidate_only", true, issues);
            RequireBoolean(root, "main_workflow_accepted", false, issues);
            RequireBoolean(root, "quality_gate_passed", false, issues);
            RequireBoolean(root, "solid_works_connected", true, issues);
            RequireBoolean(root, "real_cad_executed", true, issues);
            RequireString(root, "solid_works_version", SolidWorksVersion, issues);
            RequireString(root, "final_status", "CandidatePassed", issues);
            RequireString(root, "deliverable_status", "NotDeliverable", issues);
            RequireExactPath(root, "input_path", ResolveProjectPath(SampleInputRelativePath), issues);
            RequirePassedArtifact(root, "model", issues);
            RequirePassedArtifact(root, "step", issues);

            var currentFeatureRevision = FeatureExecutionEvidencePolicy.ComputeCurrentSourceRevision();
            var reports = GetFeatureReports(root, issues);
            ValidateCandidateFeature(
                reports,
                "plate_cut",
                FeatureTypes.ExtrudeCut,
                currentFeatureRevision,
                160d * 80d * 12d / CubicMillimetersPerCubicMeter,
                (160d * 80d * 12d - 3d * Math.PI * Math.Pow(5d, 2d) * 12d) /
                CubicMillimetersPerCubicMeter,
                issues);
            ValidateCandidateFeature(
                reports,
                "plate_hole",
                FeatureTypes.Hole,
                currentFeatureRevision,
                (160d * 80d * 12d - 3d * Math.PI * Math.Pow(5d, 2d) * 12d) /
                CubicMillimetersPerCubicMeter,
                (160d * 80d * 12d - 4d * Math.PI * Math.Pow(5d, 2d) * 12d) /
                CubicMillimetersPerCubicMeter,
                issues);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate diagnostic cannot be validated: {exception.Message}");
        }
    }

    private static IReadOnlyList<JsonElement> GetFeatureReports(JsonElement root, List<string> issues)
    {
        if (!root.TryGetProperty("feature_handler_reports", out var reports) ||
            reports.ValueKind != JsonValueKind.Array)
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate feature_handler_reports is missing.");
            return Array.Empty<JsonElement>();
        }

        return reports.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static void ValidateCandidateFeature(
        IReadOnlyList<JsonElement> reports,
        string featureId,
        string featureType,
        string currentFeatureRevision,
        double expectedVolumeBefore,
        double expectedVolumeAfter,
        List<string> issues)
    {
        var matching = reports.Where(report =>
            TryGetString(report, "feature_id", out var reportId) &&
            string.Equals(reportId, featureId, StringComparison.OrdinalIgnoreCase) &&
            TryGetString(report, "feature_type", out var reportType) &&
            string.Equals(reportType, featureType, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length != 1)
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate needs exactly one {featureId}/{featureType} report.");
            return;
        }

        var report = matching[0];
        RequireBoolean(report, "result_object_validated", true, issues);
        RequireBoolean(report, "rebuild_passed", true, issues);
        RequireBoolean(report, "geometry_change_validated", true, issues);
        RequireString(report, "adapter_id", RealSolidWorksFeatureAdapter.AdapterIdentifier, issues);
        RequireString(report, "adapter_version", RealSolidWorksFeatureAdapter.CurrentAdapterVersion, issues);
        RequireString(report, "evidence_id", "v2.0-c-diagnostic-candidate-only", issues);
        RequireString(report, "evidence_parameter_profile", "exact feature_execution_smoke input only", issues);
        RequireString(report, "evidence_solid_works_version", SolidWorksVersion, issues);
        RequireString(report, "evidence_source_revision", currentFeatureRevision, issues);

        if (!TryGetString(report, "handler_name", out var handlerName) ||
            handlerName is null ||
            !handlerName.Contains($"solidworks.{featureType}@2.0-c.2/diagnostic-candidate", StringComparison.Ordinal))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate handler binding is invalid for {featureId}/{featureType}.");
        }

        if (report.TryGetProperty("failure_stage", out var failureStage) &&
            failureStage.ValueKind != JsonValueKind.Null &&
            !string.IsNullOrWhiteSpace(failureStage.GetString()))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {featureId} has failure_stage={failureStage.GetString()}.");
        }

        if (report.TryGetProperty("issues", out var reportIssues) &&
            reportIssues.ValueKind == JsonValueKind.Array &&
            reportIssues.GetArrayLength() != 0)
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {featureId} contains issues.");
        }

        RequireNumber(report, "volume_before_cubic_meters", expectedVolumeBefore, issues);
        RequireNumber(report, "volume_after_cubic_meters", expectedVolumeAfter, issues);
    }

    private static void ValidateSketchIdentity(SolidWorksOperation operation, string expectedSketchId, List<string> issues)
    {
        if (!operation.Parameters.TryGetValue("sketch_id", out var sketchId) ||
            !string.Equals(sketchId, expectedSketchId, StringComparison.OrdinalIgnoreCase) ||
            !operation.SketchPlane.Equals("TopPlane", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"V2.0-D three-circle profile requires {expectedSketchId} on TopPlane.");
        }
    }

    private static void ValidateFeatureIdentity(
        SolidWorksOperation operation,
        string expectedFeatureId,
        string expectedFeatureType,
        List<string> issues)
    {
        if (!operation.Parameters.TryGetValue("feature_id", out var featureId) ||
            !string.Equals(featureId, expectedFeatureId, StringComparison.OrdinalIgnoreCase) ||
            !operation.Parameters.TryGetValue("feature_type", out var featureType) ||
            !string.Equals(featureType, expectedFeatureType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"V2.0-D three-circle profile requires {expectedFeatureId}/{expectedFeatureType}.");
        }
    }

    private static void ValidatePlateRectangle(
        SolidWorksOperation operation,
        double length,
        double width,
        List<string> issues)
    {
        var entities = ReadEntities(operation, "plate_profile", issues);
        if (entities.Length != 1 ||
            !entities[0].EntityType.Equals(SketchEntityTypes.Rectangle, StringComparison.OrdinalIgnoreCase) ||
            entities[0].Construction ||
            !HasNumber(entities[0], "center_x_mm", 0d) ||
            !HasNumber(entities[0], "center_y_mm", 0d) ||
            !HasNumber(entities[0], "length_mm", length) ||
            !(HasNumber(entities[0], "width_mm", width) || HasNumber(entities[0], "height_mm", width)))
        {
            issues.Add("V2.0-D three-circle profile requires the parameter-bound centered plate rectangle.");
        }
    }

    private static void ValidateCutCircles(
        SolidWorksOperation operation,
        double length,
        double width,
        double diameter,
        List<string> issues)
    {
        var entities = ReadEntities(operation, "cut_profile", issues);
        var x = length / 2d - 20d;
        var y = width / 2d - 20d;
        var expected = new[]
        {
            ("cut_hole_1", -x, -y),
            ("cut_hole_2", x, -y),
            ("cut_hole_3", -x, y)
        };
        if (entities.Length != expected.Length ||
            expected.Any(expectedCircle => !entities.Any(entity =>
                entity.EntityId.Equals(expectedCircle.Item1, StringComparison.OrdinalIgnoreCase) &&
                IsCircleAt(entity, expectedCircle.Item2, expectedCircle.Item3, diameter))))
        {
            issues.Add("V2.0-D three-circle profile requires three exact cut_profile circles at the 20 mm plate-margin coordinates.");
        }
    }

    private static void ValidateSingleHoleCircle(
        SolidWorksOperation operation,
        double length,
        double width,
        double diameter,
        List<string> issues)
    {
        var entities = ReadEntities(operation, "hole_profile", issues);
        var x = length / 2d - 20d;
        var y = width / 2d - 20d;
        if (entities.Length != 1 ||
            !entities[0].EntityId.Equals("hole_4", StringComparison.OrdinalIgnoreCase) ||
            !IsCircleAt(entities[0], x, y, diameter))
        {
            issues.Add("V2.0-D three-circle profile requires the one remaining 10 mm hole_profile circle at the positive 20 mm-margin corner.");
        }
    }

    private static void ValidateBoss(
        SolidWorksOperation boss,
        SolidWorksOperation plateSketch,
        double thickness,
        List<string> issues)
    {
        if (!boss.Parameters.TryGetValue("sketch_id", out var sketchId) ||
            !string.Equals(sketchId, "plate_profile", StringComparison.OrdinalIgnoreCase) ||
            !boss.DependsOn.Contains(plateSketch.OperationId, StringComparer.OrdinalIgnoreCase) ||
            !HasNumber(boss.Parameters, "depth_mm", thickness) ||
            !boss.Parameters.TryGetValue("direction", out var direction) ||
            !direction.Equals("blind", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("V2.0-D three-circle profile requires a blind plate_boss directly dependent on plate_profile.");
        }
    }

    private static void ValidateCut(
        SolidWorksOperation cut,
        SolidWorksOperation cutSketch,
        SolidWorksOperation boss,
        double thickness,
        List<string> issues)
    {
        if (!cut.Parameters.TryGetValue("sketch_id", out var sketchId) ||
            !string.Equals(sketchId, "cut_profile", StringComparison.OrdinalIgnoreCase) ||
            !cut.DependsOn.Contains(cutSketch.OperationId, StringComparer.OrdinalIgnoreCase) ||
            !cut.DependsOn.Contains(boss.OperationId, StringComparer.OrdinalIgnoreCase) ||
            !HasNumber(cut.Parameters, "depth_mm", thickness * 2d) ||
            !cut.Parameters.TryGetValue("through_all", out var throughAll) ||
            !bool.TryParse(throughAll, out var parsedThroughAll) ||
            parsedThroughAll)
        {
            issues.Add("V2.0-D three-circle profile requires a blind 2x-thickness plate_cut directly dependent on cut_profile and plate_boss.");
        }
    }

    private static void ValidateHole(
        SolidWorksOperation hole,
        SolidWorksOperation holeSketch,
        SolidWorksOperation cut,
        double thickness,
        double diameter,
        List<string> issues)
    {
        if (!hole.Parameters.TryGetValue("sketch_id", out var sketchId) ||
            !string.Equals(sketchId, "hole_profile", StringComparison.OrdinalIgnoreCase) ||
            !hole.DependsOn.Contains(holeSketch.OperationId, StringComparer.OrdinalIgnoreCase) ||
            !hole.DependsOn.Contains(cut.OperationId, StringComparer.OrdinalIgnoreCase) ||
            !HasNumber(hole.Parameters, "depth_mm", thickness * 2d) ||
            !(HasNumber(hole.Parameters, "diameter_mm", diameter) || HasNumber(hole.Parameters, "hole_diameter_mm", diameter)) ||
            !hole.Parameters.TryGetValue("strategy", out var strategy) ||
            !strategy.Equals("simple_circular_cut_blind", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("V2.0-D three-circle profile requires a blind diameter-matched plate_hole directly dependent on hole_profile and plate_cut.");
        }
    }

    private static SketchEntity[] ReadEntities(SolidWorksOperation operation, string sketchId, List<string> issues)
    {
        if (!operation.Parameters.TryGetValue("entities", out var entitiesJson))
        {
            issues.Add($"V2.0-D three-circle profile cannot read entities for {sketchId}.");
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<SketchEntity[]>(entitiesJson) ?? [];
        }
        catch (JsonException)
        {
            issues.Add($"V2.0-D three-circle profile entities are invalid JSON for {sketchId}.");
            return [];
        }
    }

    private static bool IsCircleAt(SketchEntity entity, double x, double y, double diameter) =>
        entity.EntityType.Equals(SketchEntityTypes.Circle, StringComparison.OrdinalIgnoreCase) &&
        !entity.Construction &&
        HasNumber(entity, "center_x_mm", x) &&
        HasNumber(entity, "center_y_mm", y) &&
        TryReadCircleDiameter(entity, out var actualDiameter) &&
        NearlyEqual(actualDiameter, diameter);

    private static bool TryReadCircleDiameter(SketchEntity entity, out double diameter)
    {
        diameter = 0d;
        var hasRadius = TryReadFiniteNumber(entity.Parameters, "radius_mm", out var radius);
        var hasDiameter = TryReadFiniteNumber(entity.Parameters, "diameter_mm", out var declaredDiameter);
        if (!hasRadius && !hasDiameter)
        {
            return false;
        }

        diameter = hasRadius ? radius * 2d : declaredDiameter;
        return diameter > 0d && (!hasRadius || !hasDiameter || NearlyEqual(diameter, declaredDiameter));
    }

    private static bool HasNumber(SketchEntity entity, string name, double expected) =>
        HasNumber(entity.Parameters, name, expected);

    private static bool HasNumber(IReadOnlyDictionary<string, string> values, string name, double expected) =>
        TryReadFiniteNumber(values, name, out var actual) && NearlyEqual(actual, expected);

    private static bool TryReadPositiveNumber(
        IReadOnlyDictionary<string, string>? values,
        string name,
        out double value)
    {
        return TryReadFiniteNumber(values, name, out value) && value > 0d;
    }

    private static bool TryReadFiniteNumber(
        IReadOnlyDictionary<string, string>? values,
        string name,
        out double value)
    {
        value = 0d;
        return values is not null &&
               values.TryGetValue(name, out var text) &&
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    private static bool IsSupportedPlateSize(double length, double width, double thickness) =>
        (NearlyEqual(length, 160d) && NearlyEqual(width, 80d) && NearlyEqual(thickness, 12d)) ||
        (NearlyEqual(length, 200d) && NearlyEqual(width, 100d) && NearlyEqual(thickness, 15d));

    private static bool NearlyEqual(double actual, double expected) =>
        Math.Abs(actual - expected) <= Tolerance * Math.Max(1d, Math.Max(Math.Abs(actual), Math.Abs(expected)));

    private static void RequirePassedArtifact(JsonElement root, string name, List<string> issues)
    {
        if (!root.TryGetProperty(name, out var artifact) || artifact.ValueKind != JsonValueKind.Object ||
            !artifact.TryGetProperty("exists", out var exists) || exists.ValueKind != JsonValueKind.True ||
            !artifact.TryGetProperty("size_bytes", out var size) || !size.TryGetInt64(out var sizeBytes) || sizeBytes <= 0 ||
            !TryGetString(artifact, "status", out var status) ||
            !string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase) ||
            !TryGetString(artifact, "file_path", out var path))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {name} artifact is not Passed and non-empty.");
            return;
        }

        try
        {
            var absolutePath = Path.GetFullPath(path!);
            if (!IsPathUnderProjectRoot(absolutePath) || !File.Exists(absolutePath) || new FileInfo(absolutePath).Length != sizeBytes)
            {
                issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {name} artifact does not physically match its report.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {name} artifact cannot be verified: {exception.Message}");
        }
    }

    private static void RequireBoolean(JsonElement root, string propertyName, bool expected, List<string> issues)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != (expected ? JsonValueKind.True : JsonValueKind.False))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {propertyName} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static void RequireString(JsonElement root, string propertyName, string expected, List<string> issues)
    {
        if (!TryGetString(root, propertyName, out var actual) ||
            !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {propertyName} must be {expected}, actual={actual ?? "missing"}.");
        }
    }

    private static void RequireExactPath(JsonElement root, string propertyName, string expected, List<string> issues)
    {
        if (!TryGetString(root, propertyName, out var actual))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {propertyName} is missing.");
            return;
        }

        try
        {
            if (!string.Equals(Path.GetFullPath(actual!), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {propertyName} does not bind the exact sample input.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {propertyName} is invalid: {exception.Message}");
        }
    }

    private static void RequireNumber(JsonElement root, string propertyName, double expected, List<string> issues)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            !value.TryGetDouble(out var actual) ||
            !double.IsFinite(actual) ||
            !NearlyEqual(actual, expected))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureApiUnverified}: V2.0-D candidate {propertyName} does not match the exact three-circle volume transition.");
        }
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ComputeFileSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string ResolveProjectPath(string path) =>
        Path.GetFullPath(Path.Combine(FindProjectRoot(), path.Replace('/', Path.DirectorySeparatorChar)));

    private static bool IsPathUnderProjectRoot(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(FindProjectRoot());
        var prefix = root + Path.DirectorySeparatorChar;
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindProjectRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("AI_Mechanical_Engineering_Agent_Platform.sln could not be located for V2.0-D profile evidence verification.");
    }

    private static string NormalizeVolatileEvidenceMetadata(string source)
    {
        return VolatileAssignmentRegex().Replace(
            source,
            match => $"{match.Groups["name"].Value} = \"<evidence-metadata>\"");
    }

    private static void Append(IncrementalHash hash, string text) =>
        hash.AppendData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)));

    [GeneratedRegex(
        "(?<name>EvidenceId|ParameterProfile|SolidWorksVersion|SampleInputRelativePath|CandidateDiagnosticRelativePath|SampleInputSha256|CandidateReportSha256|SourceRevision)\\s*=\\s*\"[^\"]*\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex VolatileAssignmentRegex();

}

public sealed record V20DThreeCircleCutEvidenceValidationResult(
    bool IsPassed,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public static V20DThreeCircleCutEvidenceValidationResult Passed() =>
        new(true, null, Array.Empty<string>());

    public static V20DThreeCircleCutEvidenceValidationResult Failed(IReadOnlyList<string> issues) =>
        new(false, PartFamilyFailureStages.FeatureApiUnverified, issues);
}
