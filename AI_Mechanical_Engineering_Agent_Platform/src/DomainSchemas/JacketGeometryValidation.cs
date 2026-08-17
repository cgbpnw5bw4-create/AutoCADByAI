namespace DomainSchemas;

public sealed record JacketGeometryValidationResult(
    bool IsValid,
    string? FailureStage,
    IReadOnlyList<string> Issues,
    double ExpectedVolumeCubicMillimeters,
    double? MeasuredVolumeCubicMillimeters);

/// <summary>
/// Pure, CAD-neutral geometry checks for a coaxial straight cylindrical jacket.
/// </summary>
public static class JacketGeometryValidator
{
    public const double DimensionToleranceMm = 0.05d;
    public const double DiameterToleranceMm = 0.02d;
    public const double VolumeRelativeTolerance = 0.002d;

    public static JacketGeometryValidationResult Validate(
        double outerDiameterMm,
        double innerDiameterMm,
        double lengthMm,
        MeasuredGeometry? measured)
    {
        var expectedVolume = Math.PI / 4d *
                             (Math.Pow(outerDiameterMm, 2d) - Math.Pow(innerDiameterMm, 2d)) *
                             lengthMm;
        var issues = new List<string>();
        string? failureStage = null;

        void Fail(string stage, string message)
        {
            failureStage ??= stage;
            issues.Add($"{stage}: {message}");
        }

        if (measured is null || (measured.ReadIssues?.Count ?? 0) > 0)
        {
            Fail(
                PartFamilyFailureStages.GeometryReadFailed,
                measured is null
                    ? "SolidWorks geometry reader returned no measurement."
                    : string.Join(" | ", measured.ReadIssues ?? Array.Empty<string>()));
        }
        else
        {
            if (!measured.RebuildPassed)
            {
                Fail(PartFamilyFailureStages.RebuildFailed, "ForceRebuild3(false) did not pass.");
            }

            if (measured.BodyCount != 1)
            {
                Fail(
                    PartFamilyFailureStages.VolumeValidationFailed,
                    $"Expected exactly one solid body, measured {measured.BodyCount?.ToString() ?? "missing"}.");
            }

            ValidateExtents(measured.ExactExtents, outerDiameterMm, lengthMm, Fail);
            ValidateCylinders(measured, outerDiameterMm, innerDiameterMm, Fail);

            if (measured.VolumeCubicMillimeters is not > 0 ||
                !double.IsFinite(measured.VolumeCubicMillimeters.Value))
            {
                Fail(PartFamilyFailureStages.VolumeValidationFailed, "Measured body volume is missing, non-finite, or non-positive.");
            }
            else
            {
                var tolerance = Math.Max(1d, expectedVolume * VolumeRelativeTolerance);
                if (Math.Abs(measured.VolumeCubicMillimeters.Value - expectedVolume) > tolerance)
                {
                    Fail(
                        PartFamilyFailureStages.VolumeValidationFailed,
                        $"Expected {expectedVolume:R} mm^3, measured {measured.VolumeCubicMillimeters.Value:R} mm^3 (tolerance {tolerance:R} mm^3).");
                }
            }
        }

        return new JacketGeometryValidationResult(
            issues.Count == 0,
            failureStage,
            issues,
            expectedVolume,
            measured?.VolumeCubicMillimeters);
    }

    private static void ValidateExtents(
        GeometryBoundingBox? exactExtents,
        double outerDiameterMm,
        double lengthMm,
        Action<string, string> fail)
    {
        if (exactExtents is null ||
            exactExtents.SortedSpansMm.Any(span => !double.IsFinite(span) || span <= 0))
        {
            fail(PartFamilyFailureStages.GeometryReadFailed, "Exact jacket extents are missing, non-finite, or non-positive.");
            return;
        }

        var expected = new[] { outerDiameterMm, outerDiameterMm, lengthMm }.OrderDescending().ToArray();
        var actual = exactExtents.SortedSpansMm;
        for (var index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index] - actual[index]) > DimensionToleranceMm)
            {
                fail(
                    PartFamilyFailureStages.ParameterGeometryMismatch,
                    $"Expected sorted extent {expected[index]:R} mm, measured {actual[index]:R} mm at index {index}.");
            }
        }
    }

    private static void ValidateCylinders(
        MeasuredGeometry measured,
        double outerDiameterMm,
        double innerDiameterMm,
        Action<string, string> fail)
    {
        var diameters = measured.Cylinders?.Select(cylinder => cylinder.DiameterMm).ToArray() ??
                        measured.CylindricalDiametersMm?.ToArray() ??
                        [];
        if (!diameters.Any(value => Math.Abs(value - outerDiameterMm) <= DiameterToleranceMm))
        {
            fail(
                PartFamilyFailureStages.ParameterGeometryMismatch,
                $"No cylindrical face matches outer_diameter_mm={outerDiameterMm:R}.");
        }

        if (!diameters.Any(value => Math.Abs(value - innerDiameterMm) <= DiameterToleranceMm))
        {
            fail(
                PartFamilyFailureStages.ParameterGeometryMismatch,
                $"No cylindrical face matches inner_diameter_mm={innerDiameterMm:R}.");
        }
    }
}
