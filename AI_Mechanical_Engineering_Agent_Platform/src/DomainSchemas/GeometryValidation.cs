using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainSchemas;

/// <summary>
/// A CAD-system-neutral snapshot produced by the SolidWorks geometry reader.
/// It deliberately contains values only: no COM object may cross this boundary.
/// </summary>
public sealed record MeasuredGeometry(
    bool RebuildPassed,
    GeometryBoundingBox? BoundingBox,
    GeometryBoundingBox? ExactExtents,
    int? BodyCount,
    double? VolumeCubicMillimeters,
    double? MassKilograms,
    double? MassPropertyVolumeCubicMillimeters,
    IReadOnlyList<string>? FeatureTypes = null,
    IReadOnlyList<MeasuredFeature>? Features = null,
    IReadOnlyList<double>? CylindricalDiametersMm = null,
    IReadOnlyList<MeasuredCylinder>? Cylinders = null,
    IReadOnlyList<string>? ReadIssues = null,
    string? SolidWorksVersion = null,
    string? GeometryEvidenceSourceRevision = null,
    IReadOnlyList<MeasuredEdge>? Edges = null);

public sealed record GeometryBoundingBox(
    double MinXmm,
    double MinYmm,
    double MinZmm,
    double MaxXmm,
    double MaxYmm,
    double MaxZmm)
{
    [JsonIgnore]
    public double XSpanMm => MaxXmm - MinXmm;

    [JsonIgnore]
    public double YSpanMm => MaxYmm - MinYmm;

    [JsonIgnore]
    public double ZSpanMm => MaxZmm - MinZmm;

    [JsonIgnore]
    public IReadOnlyList<double> SortedSpansMm =>
        new[] { XSpanMm, YSpanMm, ZSpanMm }.OrderDescending().ToArray();
}

public sealed record MeasuredFeature(string Name, string TypeName, int? ErrorCode = null);

/// <summary>
/// A measured cylindrical surface. The origin and unit direction come directly
/// from SolidWorks CylinderParams and let the validator distinguish an expected
/// plate-normal hole from an unrelated cylindrical face.
/// </summary>
public sealed record MeasuredCylinder(
    double DiameterMm,
    double OriginXmm,
    double OriginYmm,
    double OriginZmm,
    double AxisX,
    double AxisY,
    double AxisZ);

public sealed record GeometryExpectedGeometry(
    double? LengthMm,
    double? WidthMm,
    double? ThicknessMm,
    double? DiameterMm,
    int? ExpectedHoleCount,
    double? ExpectedVolumeCubicMillimeters,
    IReadOnlyList<string> ExpectedFeatureKinds,
    int ExpectedBodyCount = 1);

public sealed record GeometryDeviation(
    string Check,
    double? Expected,
    double? Measured,
    double? Tolerance,
    bool Passed,
    string? Detail = null);

public sealed record GeometryValidationReport(
    string ModelId,
    IReadOnlyDictionary<string, string> InputParameters,
    MeasuredGeometry? MeasuredGeometry,
    GeometryExpectedGeometry ExpectedGeometry,
    IReadOnlyList<GeometryDeviation> Deviations,
    IReadOnlyList<string> PassedChecks,
    IReadOnlyList<string> FailedChecks,
    string? FailureStage,
    string FinalStatus,
    DateTimeOffset GeneratedAt,
    string? SolidWorksVersion = null,
    string? GeometryEvidenceSourceRevision = null);

public sealed record RebuildReport(
    string ModelId,
    IReadOnlyDictionary<string, string> OldParameters,
    IReadOnlyDictionary<string, string> NewParameters,
    IReadOnlyList<string> ChangedFeatures,
    RebuildResultSummary RebuildResult,
    string? FailureStage,
    string FinalStatus,
    DateTimeOffset GeneratedAt);

public sealed record RebuildResultSummary(
    string InitialStatus,
    string UpdatedStatus,
    bool FeatureGraphPreserved,
    string? InitialGeometryReportPath,
    string? UpdatedGeometryReportPath,
    string? Detail = null);

/// <summary>
/// Pure Geometry Validation layer. It consumes measured values produced by a
/// CAD adapter and never accesses SolidWorks COM directly.
/// </summary>
public sealed class GeometryValidator
{
    public const double DimensionToleranceMm = 0.05d;
    public const double VolumeRelativeTolerance = 0.002d;
    public const double DiameterToleranceMm = 0.02d;

    public GeometryValidationReport Validate(
        CADModelSpec spec,
        MeasuredGeometry? measured,
        IReadOnlyList<string>? executedFeatureTypes = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var expected = BuildExpectedGeometry(spec);
        var passed = new List<string>();
        var failed = new List<string>();
        var deviations = new List<GeometryDeviation>();
        string? failureStage = null;

        void Fail(string stage, string check, string detail)
        {
            failureStage ??= stage;
            failed.Add($"{check}: {detail}");
        }

        if (measured is null || (measured.ReadIssues?.Count ?? 0) > 0)
        {
            Fail(
                PartFamilyFailureStages.GeometryReadFailed,
                "geometry_read",
                measured is null
                    ? "The SolidWorks geometry reader returned no snapshot."
                    : string.Join(" | ", measured.ReadIssues ?? Array.Empty<string>()));
        }
        else
        {
            if (!measured.RebuildPassed)
            {
                Fail(PartFamilyFailureStages.RebuildFailed, "solidworks_rebuild", "ForceRebuild3(false) did not pass.");
            }
            else
            {
                passed.Add("solidworks_rebuild");
            }

            ValidateBoundingBox(measured, expected, deviations, passed, Fail);
            ValidateBodiesAndVolume(measured, expected, deviations, passed, Fail);
            ValidateFeatureResults(measured, expected, executedFeatureTypes, passed, Fail);
            ValidateDiameter(measured, expected, deviations, passed, Fail);
        }

        var finalStatus = failed.Count == 0 ? "Passed" : "Failed";
        return new GeometryValidationReport(
            spec.ModelId,
            Copy(spec.Parameters),
            measured,
            expected,
            deviations,
            passed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            failed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            failureStage,
            finalStatus,
            DateTimeOffset.UtcNow,
            measured?.SolidWorksVersion,
            measured?.GeometryEvidenceSourceRevision);
    }

    public GeometryValidationReport Validate(
        SolidWorksBuildPlan plan,
        MeasuredGeometry? measured,
        IReadOnlyList<string>? executedFeatureTypes = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var features = plan.Operations
            .Where(operation =>
                !operation.OperationType.Equals("SavePart", StringComparison.OrdinalIgnoreCase) &&
                !operation.OperationType.Equals("ExportStep", StringComparison.OrdinalIgnoreCase))
            .Select(operation => new FeatureDefinition(
                operation.Parameters.GetValueOrDefault("feature_id") ?? operation.OperationId,
                operation.Parameters.GetValueOrDefault("feature_type") ?? string.Empty,
                operation.Parameters,
                operation.DependsOn,
                operation.Parameters.TryGetValue("sketch_id", out var sketchId) ? [sketchId] : [],
                referencedFeatures: []))
            .Where(feature => !string.IsNullOrWhiteSpace(feature.FeatureType))
            .ToArray();
        var spec = new CADModelSpec(
            plan.SourceCadModelSpecId,
            plan.PartType,
            plan.Unit,
            plan.Dimensions,
            features: features);
        return Validate(spec, measured, executedFeatureTypes);
    }

    public static GeometryExpectedGeometry BuildExpectedGeometry(CADModelSpec spec)
    {
        var length = ReadNumber(spec, "length_mm");
        var width = ReadNumber(spec, "width_mm");
        var thickness = ReadNumber(spec, "thickness_mm");
        var diameter = ReadNumber(spec, "diameter_mm") ?? ReadNumber(spec, "hole_diameter_mm");
        var count = ReadInteger(spec, "hole_count");
        double? expectedVolume = null;
        if (length is > 0 && width is > 0 && thickness is > 0)
        {
            expectedVolume = length.Value * width.Value * thickness.Value;
            if (diameter is > 0 && count is > 0)
            {
                expectedVolume -= count.Value * Math.PI * Math.Pow(diameter.Value / 2d, 2d) * thickness.Value;
            }
        }

        var kinds = new List<string> { "sketch", "extrude_boss" };
        if (spec.Features.Any(feature => feature.FeatureType.Equals(FeatureTypes.ExtrudeCut, StringComparison.OrdinalIgnoreCase)))
        {
            kinds.Add("extrude_cut");
        }

        if (spec.Features.Any(feature => feature.FeatureType.Equals(FeatureTypes.Hole, StringComparison.OrdinalIgnoreCase)))
        {
            kinds.Add("hole");
        }

        return new GeometryExpectedGeometry(length, width, thickness, diameter, count, expectedVolume, kinds);
    }

    public static void WriteReport(string path, GeometryValidationReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions()));
    }

    public static void WriteRebuildReport(string path, RebuildReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions()));
    }

    private static void ValidateBoundingBox(
        MeasuredGeometry measured,
        GeometryExpectedGeometry expected,
        List<GeometryDeviation> deviations,
        List<string> passed,
        Action<string, string, string> fail)
    {
        var boundingBox = measured.BoundingBox;
        var exact = measured.ExactExtents;
        if (!IsValidBox(boundingBox))
        {
            fail(PartFamilyFailureStages.BoundingBoxInvalid, "bounding_box", "BoundingBox has missing, non-finite, or non-positive spans.");
            return;
        }

        passed.Add("bounding_box");
        if (!IsValidBox(exact))
        {
            fail(PartFamilyFailureStages.GeometryReadFailed, "exact_extents", "GetExtremePoint did not return three finite positive extents.");
            return;
        }

        passed.Add("exact_extents");
        var measuredSpans = exact!.SortedSpansMm;
        var expectedSpans = new[] { expected.LengthMm, expected.WidthMm, expected.ThicknessMm };
        var labels = new[] { "length_mm", "width_mm", "thickness_mm" };
        for (var index = 0; index < expectedSpans.Length; index++)
        {
            if (expectedSpans[index] is not > 0)
            {
                continue;
            }

            var actual = measuredSpans[index];
            var target = expectedSpans[index]!.Value;
            var checkPassed = Math.Abs(actual - target) <= DimensionToleranceMm;
            deviations.Add(new GeometryDeviation(labels[index], target, actual, DimensionToleranceMm, checkPassed));
            if (checkPassed)
            {
                passed.Add($"parameter_geometry:{labels[index]}");
            }
            else
            {
                fail(
                    PartFamilyFailureStages.ParameterGeometryMismatch,
                    $"parameter_geometry:{labels[index]}",
                    $"expected {target:R} mm, measured {actual:R} mm.");
            }
        }
    }

    private static void ValidateBodiesAndVolume(
        MeasuredGeometry measured,
        GeometryExpectedGeometry expected,
        List<GeometryDeviation> deviations,
        List<string> passed,
        Action<string, string, string> fail)
    {
        if (measured.BodyCount is not > 0)
        {
            fail(PartFamilyFailureStages.VolumeValidationFailed, "body_count", "No positive solid-body count was measured.");
        }
        else if (measured.BodyCount != expected.ExpectedBodyCount)
        {
            fail(
                PartFamilyFailureStages.VolumeValidationFailed,
                "body_count",
                $"Expected {expected.ExpectedBodyCount} solid body, measured {measured.BodyCount.Value}.");
        }
        else
        {
            passed.Add("body_count");
        }

        if (measured.VolumeCubicMillimeters is not > 0 || !double.IsFinite(measured.VolumeCubicMillimeters.Value))
        {
            fail(PartFamilyFailureStages.VolumeValidationFailed, "volume", "Solid-body volume is missing, non-finite, or non-positive.");
            return;
        }

        passed.Add("volume");
        if (measured.MassPropertyVolumeCubicMillimeters is > 0)
        {
            var massPropertyVolume = measured.MassPropertyVolumeCubicMillimeters.Value;
            var allowed = Math.Max(1d, Math.Abs(measured.VolumeCubicMillimeters.Value) * VolumeRelativeTolerance);
            var matchesBodyVolume = Math.Abs(massPropertyVolume - measured.VolumeCubicMillimeters.Value) <= allowed;
            deviations.Add(new GeometryDeviation(
                "mass_property_volume",
                measured.VolumeCubicMillimeters.Value,
                massPropertyVolume,
                allowed,
                matchesBodyVolume));
            if (matchesBodyVolume)
            {
                passed.Add("mass_property_volume");
            }
            else
            {
                fail(PartFamilyFailureStages.VolumeValidationFailed, "mass_property_volume", "MassProperty volume disagrees with total body volume.");
            }
        }

        if (expected.ExpectedVolumeCubicMillimeters is not > 0)
        {
            return;
        }

        var target = expected.ExpectedVolumeCubicMillimeters.Value;
        var allowedVolume = Math.Max(1d, Math.Abs(target) * VolumeRelativeTolerance);
        var volumeMatches = Math.Abs(measured.VolumeCubicMillimeters.Value - target) <= allowedVolume;
        deviations.Add(new GeometryDeviation(
            "volume_cubic_mm",
            target,
            measured.VolumeCubicMillimeters.Value,
            allowedVolume,
            volumeMatches));
        if (volumeMatches)
        {
            passed.Add("volume_expected_geometry");
        }
        else
        {
            fail(
                PartFamilyFailureStages.VolumeValidationFailed,
                "volume_expected_geometry",
                $"expected {target:R} mm^3, measured {measured.VolumeCubicMillimeters.Value:R} mm^3.");
        }
    }

    private static void ValidateFeatureResults(
        MeasuredGeometry measured,
        GeometryExpectedGeometry expected,
        IReadOnlyList<string>? executedFeatureTypes,
        List<string> passed,
        Action<string, string, string> fail)
    {
        var treeTypes = measured.FeatureTypes ?? Array.Empty<string>();
        var executed = executedFeatureTypes ?? Array.Empty<string>();
        var sketchExists = treeTypes.Any(IsSketchType);
        var bossExists = treeTypes.Any(IsBossType);
        var cutExists = treeTypes.Any(IsCutType);
        var candidateDiameters = ExpectedCylinderDiameters(measured, expected);
        var holeGeometryCount = expected.ExpectedHoleCount is > 0 && expected.DiameterMm is > 0
            ? candidateDiameters.Count(value => Math.Abs(value - expected.DiameterMm.Value) <= DiameterToleranceMm)
            : 0;

        ValidateFeature("sketch", sketchExists, executed, "sketch", passed, fail);
        ValidateFeature("extrude", bossExists, executed, FeatureTypes.ExtrudeBoss, passed, fail);
        if (expected.ExpectedFeatureKinds.Contains("extrude_cut", StringComparer.OrdinalIgnoreCase))
        {
            ValidateFeature("cut", cutExists, executed, FeatureTypes.ExtrudeCut, passed, fail);
        }

        if (expected.ExpectedFeatureKinds.Contains("hole", StringComparer.OrdinalIgnoreCase))
        {
            var handlerExecuted = executed.Contains(FeatureTypes.Hole, StringComparer.OrdinalIgnoreCase);
            var expectedHoleCount = expected.ExpectedHoleCount ?? 1;
            var holeExists = cutExists && handlerExecuted && holeGeometryCount >= expectedHoleCount;
            if (holeExists)
            {
                passed.Add("feature_result:hole");
            }
            else
            {
                fail(
                    PartFamilyFailureStages.FeatureMissingAfterRebuild,
                    "feature_result:hole",
                    $"Expected {expectedHoleCount} cylindrical faces at the requested diameter; measured {holeGeometryCount}.");
            }
        }
    }

    private static void ValidateFeature(
        string label,
        bool treeExists,
        IReadOnlyList<string> executed,
        string expectedHandlerType,
        List<string> passed,
        Action<string, string, string> fail)
    {
        if (treeExists && executed.Contains(expectedHandlerType, StringComparer.OrdinalIgnoreCase))
        {
            passed.Add($"feature_result:{label}");
        }
        else
        {
            fail(
                PartFamilyFailureStages.FeatureMissingAfterRebuild,
                $"feature_result:{label}",
                "The FeatureTree and executed FeatureHandler evidence do not both confirm the expected result.");
        }
    }

    private static void ValidateDiameter(
        MeasuredGeometry measured,
        GeometryExpectedGeometry expected,
        List<GeometryDeviation> deviations,
        List<string> passed,
        Action<string, string, string> fail)
    {
        if (expected.DiameterMm is not > 0)
        {
            return;
        }

        var diameters = ExpectedCylinderDiameters(measured, expected);
        if (diameters.Count == 0)
        {
            fail(
                PartFamilyFailureStages.ParameterGeometryMismatch,
                "parameter_geometry:diameter_mm",
                "No cylindrical face was measured for diameter validation.");
            return;
        }

        var measuredDiameter = diameters
            .OrderBy(value => Math.Abs(value - expected.DiameterMm.Value))
            .First();
        var matches = Math.Abs(measuredDiameter - expected.DiameterMm.Value) <= DiameterToleranceMm;
        deviations.Add(new GeometryDeviation(
            "diameter_mm",
            expected.DiameterMm.Value,
            measuredDiameter,
            DiameterToleranceMm,
            matches));
        if (matches)
        {
            passed.Add("parameter_geometry:diameter_mm");
        }
        else
        {
            fail(
                PartFamilyFailureStages.ParameterGeometryMismatch,
                "parameter_geometry:diameter_mm",
                $"expected {expected.DiameterMm.Value:R} mm, measured {measuredDiameter:R} mm.");
        }
    }

    private static bool IsSketchType(string value) =>
        value.Contains("ProfileFeature", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Sketch", StringComparison.OrdinalIgnoreCase);

    private static bool IsBossType(string value) =>
        value.Contains("Boss", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Extrusion", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Extrude", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("Cut", StringComparison.OrdinalIgnoreCase);

    private static bool IsCutType(string value) =>
        value.Contains("Cut", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("ICE", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<double> ExpectedCylinderDiameters(
        MeasuredGeometry measured,
        GeometryExpectedGeometry expected)
    {
        var cylinders = measured.Cylinders;
        if (cylinders is null || cylinders.Count == 0)
        {
            // Compatibility for stored reports created before the V2.0-D reader
            // recorded CylinderParams. The real reader always emits full records.
            return measured.CylindricalDiametersMm ?? Array.Empty<double>();
        }

        var extents = measured.ExactExtents;
        if (!IsValidBox(extents))
        {
            return Array.Empty<double>();
        }

        var spans = new[] { extents!.XSpanMm, extents.YSpanMm, extents.ZSpanMm };
        var plateNormal = Array.IndexOf(spans, spans.Min());
        return cylinders
            .Where(cylinder =>
                new[]
                {
                    cylinder.DiameterMm,
                    cylinder.OriginXmm,
                    cylinder.OriginYmm,
                    cylinder.OriginZmm,
                    cylinder.AxisX,
                    cylinder.AxisY,
                    cylinder.AxisZ
                }.All(double.IsFinite))
            .Where(cylinder => Math.Abs(new[] { cylinder.AxisX, cylinder.AxisY, cylinder.AxisZ }[plateNormal]) >= 0.9d)
            .Select(cylinder => cylinder.DiameterMm)
            .ToArray();
    }

    private static bool IsValidBox(GeometryBoundingBox? box) =>
        box is not null &&
        new[] { box.MinXmm, box.MinYmm, box.MinZmm, box.MaxXmm, box.MaxYmm, box.MaxZmm }.All(double.IsFinite) &&
        box.XSpanMm > 0 && box.YSpanMm > 0 && box.ZSpanMm > 0;

    private static double? ReadNumber(CADModelSpec spec, string name) =>
        spec.TryGetParameter(name, out var value) &&
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed)
            ? parsed
            : null;

    private static int? ReadInteger(CADModelSpec spec, string name) =>
        spec.TryGetParameter(name, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string> values) =>
        new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
