namespace DomainSchemas;

/// <summary>
/// 零件族声明的理论几何期望。由零件族从自身参数推导，
/// 与 CAD API 无关，可独立单测。
/// </summary>
public sealed record ExpectedPartGeometry(
    int BodyCount,
    double VolumeCubicMillimeters,
    double VolumeRelativeTolerance);

public sealed record PartGeometryValidationResult(
    bool IsValid,
    string? FailureStage,
    IReadOnlyList<string> Issues,
    double ExpectedVolumeCubicMillimeters,
    double? MeasuredVolumeCubicMillimeters);

/// <summary>
/// 平台级几何判定。V2.0-D 的几何校验原本挂在零件专用 Builder 上，
/// V2.0-E 统一执行路线后该链路两端同时不可达。这里把判定提升为
/// 所有零件族共用的后置阶段：读取由通用 GeometryReader 完成，
/// 期望值由零件族声明，判定逻辑只有这一份。
/// </summary>
public static class PartGeometryValidator
{
    public static PartGeometryValidationResult Validate(
        ExpectedPartGeometry expected,
        MeasuredGeometry? measured)
    {
        ArgumentNullException.ThrowIfNull(expected);
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
                measured?.ReadIssues?.FirstOrDefault() ?? "measured geometry is unavailable.");
            return new(false, failureStage, issues, expected.VolumeCubicMillimeters, measured?.VolumeCubicMillimeters);
        }

        if (measured.BodyCount is not { } measuredBodyCount)
        {
            Fail(
                PartFamilyFailureStages.GeometryReadFailed,
                "measured body count is unavailable.");
        }
        else if (measuredBodyCount != expected.BodyCount)
        {
            Fail(
                PartFamilyFailureStages.ParameterGeometryMismatch,
                $"measured body count {measuredBodyCount} does not match expected {expected.BodyCount}.");
        }

        var measuredVolume = measured.VolumeCubicMillimeters;
        if (measuredVolume is null || !double.IsFinite(measuredVolume.Value) || measuredVolume.Value <= 0d)
        {
            Fail(
                PartFamilyFailureStages.VolumeValidationFailed,
                "measured volume is missing or not a positive finite number.");
        }
        else
        {
            // 绝对下限 1 mm^3 避免极小零件被浮点噪声判失败。
            var tolerance = Math.Max(1d, expected.VolumeCubicMillimeters * expected.VolumeRelativeTolerance);
            if (Math.Abs(expected.VolumeCubicMillimeters - measuredVolume.Value) > tolerance)
            {
                Fail(
                    PartFamilyFailureStages.VolumeValidationFailed,
                    $"Expected volume {expected.VolumeCubicMillimeters:R} mm^3 but measured " +
                    $"{measuredVolume.Value:R} mm^3, which exceeds tolerance {tolerance:R} mm^3.");
            }
        }

        return new(
            issues.Count == 0,
            failureStage,
            issues,
            expected.VolumeCubicMillimeters,
            measuredVolume);
    }
}
