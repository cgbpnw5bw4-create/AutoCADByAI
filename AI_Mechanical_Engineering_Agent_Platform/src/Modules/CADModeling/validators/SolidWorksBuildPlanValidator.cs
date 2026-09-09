using DomainSchemas;
using QualityGate;

namespace PlatformCore.Modules.CADModeling.Validators;

public sealed class SolidWorksBuildPlanValidator : IValidator
{
    private static readonly HashSet<string> AllowedOperationTypes = new(
        BuildPlanCompiler.FeatureOperationMappings.Values.Concat(
        [
            "CreateSketch",
            "CreateCenterLine",
            "CreateHole",
            "ExportStep",
            "SavePart"
        ]),
        StringComparer.OrdinalIgnoreCase);

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
            issues.Add("non_retryable: invalid_cad_model_spec: target_cad_system must be SolidWorks.");
        }

        if (string.IsNullOrWhiteSpace(plan.Unit))
        {
            issues.Add("non_retryable: invalid_cad_model_spec: unit is required.");
        }

        if (plan.Operations.Count == 0)
        {
            issues.Add("non_retryable: invalid_cad_model_spec: operations must not be empty.");
        }

        foreach (var operation in plan.Operations)
        {
            if (!AllowedOperationTypes.Contains(operation.OperationType))
            {
                issues.Add($"non_retryable: {PartFamilyFailureStages.UnsupportedFeatureType}: operation_type is not allowed: {operation.OperationType}.");
            }
        }

        ValidateOperationGraph(plan.Operations, issues);
        var holeValidation = HolePlanValidation.Validate(plan);
        if (holeValidation is not null) issues.AddRange(holeValidation.Issues.Select(issue => $"non_retryable: {issue}"));

        if (plan.ExpectedArtifacts.Count == 0)
        {
            issues.Add("non_retryable: invalid_cad_model_spec: expected_artifacts must not be empty.");
        }

        foreach (var artifact in plan.ExpectedArtifacts)
        {
            if (!AllowedExtensions.Contains(artifact.ExpectedExtension))
            {
                issues.Add($"non_retryable: invalid_cad_model_spec: artifact extension is not allowed: {artifact.ExpectedExtension}.");
            }
        }
    }

    private static void ValidateOperationGraph(
        IReadOnlyList<SolidWorksOperation> operations,
        List<string> issues)
    {
        var invalidId = operations.FirstOrDefault(operation => string.IsNullOrWhiteSpace(operation.OperationId));
        if (invalidId is not null)
        {
            issues.Add($"non_retryable: {PartFamilyFailureStages.InvalidCadModelSpec}: operation_id is required.");
            return;
        }

        var duplicateId = operations
            .GroupBy(operation => operation.OperationId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            issues.Add($"non_retryable: {PartFamilyFailureStages.InvalidCadModelSpec}: duplicate operation_id {duplicateId.Key}.");
            return;
        }

        var byId = operations.ToDictionary(operation => operation.OperationId, StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operations)
        {
            foreach (var dependency in operation.DependsOn)
            {
                if (!byId.ContainsKey(dependency))
                {
                    issues.Add($"non_retryable: {PartFamilyFailureStages.FeatureDependencyMissing}: operation {operation.OperationId} depends on missing operation {dependency}.");
                    return;
                }

                if (dependency.Equals(operation.OperationId, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"non_retryable: {PartFamilyFailureStages.FeatureDependencyCycle}: operation {operation.OperationId} depends on itself.");
                    return;
                }
            }
        }

        var indegree = operations.ToDictionary(
            operation => operation.OperationId,
            operation => operation.DependsOn.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            StringComparer.OrdinalIgnoreCase);
        var dependents = operations.ToDictionary(
            operation => operation.OperationId,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operations)
        {
            foreach (var dependency in operation.DependsOn.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                dependents[dependency].Add(operation.OperationId);
            }
        }

        var ready = new Queue<string>(indegree.Where(item => item.Value == 0).Select(item => item.Key));
        var visited = 0;
        while (ready.TryDequeue(out var operationId))
        {
            visited++;
            foreach (var dependent in dependents[operationId])
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (visited != operations.Count)
        {
            issues.Add($"non_retryable: {PartFamilyFailureStages.FeatureDependencyCycle}: BuildPlan operation graph contains a cycle.");
            return;
        }

        var sourceOrder = operations
            .Select((operation, index) => (operation.OperationId, Index: index))
            .ToDictionary(item => item.OperationId, item => item.Index, StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operations)
        {
            var outOfOrderDependency = operation.DependsOn.FirstOrDefault(
                dependency => sourceOrder[dependency] >= sourceOrder[operation.OperationId]);
            if (!string.IsNullOrWhiteSpace(outOfOrderDependency))
            {
                issues.Add($"non_retryable: {PartFamilyFailureStages.InvalidFeatureOrder}: dependency {outOfOrderDependency} must precede operation {operation.OperationId}.");
                return;
            }
        }
    }

    private static bool IsNonRetryable(string issue) =>
        HasIssueMarker(issue, "non_retryable") ||
        HasIssueMarker(issue, "critical");

    private static bool HasIssueMarker(string issue, string marker) =>
        issue.Equals(marker, StringComparison.OrdinalIgnoreCase) ||
        issue.StartsWith($"{marker}:", StringComparison.OrdinalIgnoreCase) ||
        issue.StartsWith($"[{marker}]", StringComparison.OrdinalIgnoreCase);
}
