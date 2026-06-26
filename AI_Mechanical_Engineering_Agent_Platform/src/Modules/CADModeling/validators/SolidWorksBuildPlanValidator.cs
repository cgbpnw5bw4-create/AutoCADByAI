using DomainSchemas;
using QualityGate;

namespace PlatformCore.Modules.CADModeling.Validators;

public sealed class SolidWorksBuildPlanValidator : IValidator
{
    private static readonly HashSet<string> AllowedOperationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreateSketch",
        "ExtrudeBoss",
        "CutExtrude",
        "AddFillet",
        "AddChamfer",
        "AddHoleWizardHole",
        "ExportStep",
        "SavePart"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".SLDPRT",
        ".STEP",
        ".SLDDRW",
        ".PDF",
        ".json"
    };

    public string Name => "solidworks-build-plan-validator";

    public ReviewReport Validate(object payload)
    {
        var issues = new List<string>();
        SolidWorksBuildPlan? plan = null;

        switch (payload)
        {
            case SolidWorksBuildPlan buildPlan:
                plan = buildPlan;
                break;
            case SolidWorksWorkerRequest request:
                plan = request.BuildPlan;
                if (!request.DryRun)
                {
                    issues.Add("dry_run must remain true for V0.9-B SolidWorks skeleton.");
                }

                if (request.AllowRealCadExecution)
                {
                    issues.Add("allow_real_cad_execution must remain false for V0.9-B SolidWorks skeleton.");
                }

                break;
            default:
                issues.Add("payload must be SolidWorksBuildPlan or SolidWorksWorkerRequest.");
                break;
        }

        if (plan is not null)
        {
            ValidatePlan(plan, issues);
        }

        return new ReviewReport(
            $"solidworks-build-plan-validation-{Guid.NewGuid():N}",
            Name,
            issues.Count == 0,
            issues.Count == 0 ? 0.95 : 0.2,
            issues,
            RequiresHumanApproval: false,
            HasFatalError: issues.Any(IsNonRetryable));
    }

    private static void ValidatePlan(SolidWorksBuildPlan plan, List<string> issues)
    {
        if (!string.Equals(plan.TargetCadSystem, "SolidWorks", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("target_cad_system must be SolidWorks.");
        }

        if (string.IsNullOrWhiteSpace(plan.Unit))
        {
            issues.Add("unit is required.");
        }

        if (plan.Operations.Count == 0)
        {
            issues.Add("operations must not be empty.");
        }

        foreach (var operation in plan.Operations)
        {
            if (!AllowedOperationTypes.Contains(operation.OperationType))
            {
                issues.Add($"operation_type is not allowed: {operation.OperationType}.");
            }
        }

        if (plan.ExpectedArtifacts.Count == 0)
        {
            issues.Add("expected_artifacts must not be empty.");
        }

        foreach (var artifact in plan.ExpectedArtifacts)
        {
            if (!AllowedExtensions.Contains(artifact.ExpectedExtension))
            {
                issues.Add($"artifact extension is not allowed: {artifact.ExpectedExtension}.");
            }
        }
    }

    private static bool IsNonRetryable(string issue) =>
        issue.Contains("allow_real_cad_execution", StringComparison.OrdinalIgnoreCase) ||
        issue.Contains("non_retryable", StringComparison.OrdinalIgnoreCase) ||
        issue.Contains("critical", StringComparison.OrdinalIgnoreCase);
}
