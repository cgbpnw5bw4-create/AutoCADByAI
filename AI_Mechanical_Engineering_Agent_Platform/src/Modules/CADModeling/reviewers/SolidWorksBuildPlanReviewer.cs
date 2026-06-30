using DomainSchemas;
using QualityGate;

namespace PlatformCore.Modules.CADModeling.Reviewers;

public sealed class SolidWorksBuildPlanReviewer : IReviewer
{
    public string Name => "solidworks-build-plan-reviewer";

    public ReviewReport Review(object payload)
    {
        var issues = new List<string>();
        if (payload is not SolidWorksBuildPlan plan)
        {
            issues.Add("payload must be SolidWorksBuildPlan.");
            return Report(issues);
        }

        var dimensions = ExtractDimensions(plan, issues);
        if (dimensions.LengthMm is <= 0 || dimensions.WidthMm is <= 0 || dimensions.ThicknessMm is <= 0)
        {
            issues.Add("plate length, width and thickness must be positive.");
        }

        if (dimensions.HoleDiameterMm is <= 0)
        {
            issues.Add("hole diameter must be positive.");
        }

        if (dimensions.HoleCount is not 4)
        {
            issues.Add("plate_basic_4holes must contain four holes.");
        }

        if (dimensions.HoleDiameterMm > 0 &&
            dimensions.WidthMm > 0 &&
            dimensions.HoleDiameterMm > dimensions.WidthMm / 2)
        {
            issues.Add("hole diameter is too large for the simplified plate boundary rule.");
        }

        if (dimensions.LengthMm > 0 &&
            dimensions.WidthMm > 0 &&
            dimensions.HoleDiameterMm > 0 &&
            !FourHolePatternFitsInsidePlate(plan, dimensions))
        {
            issues.Add("four-hole pattern may exceed the simplified plate boundary.");
        }

        if (!plan.ExpectedArtifacts.Any(artifact => string.Equals(artifact.ExpectedExtension, ".SLDPRT", StringComparison.OrdinalIgnoreCase)) ||
            !plan.ExpectedArtifacts.Any(artifact => string.Equals(artifact.ExpectedExtension, ".STEP", StringComparison.OrdinalIgnoreCase)) ||
            !plan.ExpectedArtifacts.Any(artifact => artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("expected output formats must include .SLDPRT, .STEP and build_report.json.");
        }

        if (plan.RiskNotes.Any(note => note.Contains("real CAD execution enabled", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("real CAD execution risk must be controlled by an explicit worker request, not by BuildPlan text.");
        }

        return Report(issues);
    }

    private static (double? LengthMm, double? WidthMm, double? ThicknessMm, double? HoleDiameterMm, int? HoleCount) ExtractDimensions(
        SolidWorksBuildPlan plan,
        List<string> issues)
    {
        var sketch = plan.Operations.FirstOrDefault(operation =>
            operation.OperationType.Equals("CreateSketch", StringComparison.OrdinalIgnoreCase) &&
            operation.Parameters.ContainsKey("length_mm"));
        var extrude = plan.Operations.FirstOrDefault(operation =>
            operation.OperationType.Equals("ExtrudeBoss", StringComparison.OrdinalIgnoreCase));
        var cut = plan.Operations.FirstOrDefault(operation =>
            operation.OperationType.Equals("CutExtrude", StringComparison.OrdinalIgnoreCase));

        return (
            ParseDouble(sketch, "length_mm", issues),
            ParseDouble(sketch, "width_mm", issues),
            ParseDouble(extrude, "depth_mm", issues),
            ParseDouble(cut, "hole_diameter_mm", issues),
            ParseInt(cut, "hole_count", issues));
    }

    private static bool FourHolePatternFitsInsidePlate(
        SolidWorksBuildPlan plan,
        (double? LengthMm, double? WidthMm, double? ThicknessMm, double? HoleDiameterMm, int? HoleCount) dimensions)
    {
        var holeSketch = plan.Operations.FirstOrDefault(operation =>
            operation.OperationType.Equals("CreateSketch", StringComparison.OrdinalIgnoreCase) &&
            operation.Parameters.ContainsKey("margin_x_mm") &&
            operation.Parameters.ContainsKey("margin_y_mm"));
        if (holeSketch is null)
        {
            return false;
        }

        var marginX = ParseDoubleOrDefault(holeSketch, "margin_x_mm", 0);
        var marginY = ParseDoubleOrDefault(holeSketch, "margin_y_mm", 0);
        var radius = dimensions.HoleDiameterMm.GetValueOrDefault() / 2;

        return marginX > radius &&
               marginY > radius &&
               marginX < dimensions.LengthMm.GetValueOrDefault() / 2 &&
               marginY < dimensions.WidthMm.GetValueOrDefault() / 2;
    }

    private static double? ParseDouble(SolidWorksOperation? operation, string key, List<string> issues)
    {
        if (operation is null)
        {
            issues.Add($"operation containing {key} was not found.");
            return null;
        }

        if (!operation.Parameters.TryGetValue(key, out var value) ||
            !double.TryParse(value, out var parsed))
        {
            issues.Add($"parameter {key} is missing or invalid.");
            return null;
        }

        return parsed;
    }

    private static int? ParseInt(SolidWorksOperation? operation, string key, List<string> issues)
    {
        if (operation is null)
        {
            issues.Add($"operation containing {key} was not found.");
            return null;
        }

        if (!operation.Parameters.TryGetValue(key, out var value) ||
            !int.TryParse(value, out var parsed))
        {
            issues.Add($"parameter {key} is missing or invalid.");
            return null;
        }

        return parsed;
    }

    private static double ParseDoubleOrDefault(SolidWorksOperation operation, string key, double fallback) =>
        operation.Parameters.TryGetValue(key, out var value) &&
        double.TryParse(value, out var parsed)
            ? parsed
            : fallback;

    private static ReviewReport Report(IReadOnlyList<string> issues) =>
        new(
            $"solidworks-build-plan-review-{Guid.NewGuid():N}",
            "solidworks-build-plan-reviewer",
            issues.Count == 0,
            issues.Count == 0 ? 0.95 : 0.2,
            issues,
            RequiresHumanApproval: false,
            HasFatalError: false);
}
