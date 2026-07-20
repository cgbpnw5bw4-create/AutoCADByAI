using DomainSchemas;
using QualityGate;

namespace PlatformCore.Modules.CADModeling.Reviewers;

public sealed class SolidWorksBuildPlanReviewer : IReviewer
{
    private readonly PartTypeRegistry _partTypeRegistry;

    public SolidWorksBuildPlanReviewer(PartTypeRegistry? partTypeRegistry = null)
    {
        _partTypeRegistry = partTypeRegistry ?? PartTypeRegistry.CreateDefault();
    }

    public string Name => "solidworks-build-plan-reviewer";

    public ReviewReport Review(object payload)
    {
        var issues = new List<string>();
        if (payload is not SolidWorksBuildPlan plan)
        {
            issues.Add("payload must be SolidWorksBuildPlan.");
            return Report(issues, fatal: true);
        }

        if (!_partTypeRegistry.TryGetDefinition(plan.PartType, out var definition))
        {
            issues.Add($"{PartFamilyFailureStages.UnsupportedPartType}: {plan.PartType} is not registered.");
            return Report(issues, fatal: true);
        }

        issues.AddRange(definition.ReviewBuildPlan(plan));
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

        return Report(issues, fatal: false);
    }

    private static ReviewReport Report(IReadOnlyList<string> issues, bool fatal) =>
        new(
            $"solidworks-build-plan-review-{Guid.NewGuid():N}",
            "solidworks-build-plan-reviewer",
            issues.Count == 0,
            issues.Count == 0 ? 0.95 : 0.2,
            issues,
            RequiresHumanApproval: false,
            HasFatalError: fatal && issues.Count > 0);
}
