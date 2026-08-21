namespace AgentContracts;

/// <summary>
/// Declarative route supplied to the chief-engineer orchestration boundary.
/// The orchestrator owns workflow execution, not the product-specific agent
/// sequence, so hosts and tests can select an explicit route without editing
/// orchestration code.
/// </summary>
public sealed record InternalWorkflowRoute(IReadOnlyList<string> AgentIds)
{
    public static InternalWorkflowRoute EngineeringDefault { get; } = new(
    [
        "mechanical-designer",
        "cad-modeler",
        "drawing-engineer",
        "drawing-reviewer"
    ]);

    public IReadOnlyList<string> Validate()
    {
        var issues = AgentIds
            .Where(string.IsNullOrWhiteSpace)
            .Select(_ => "internal_workflow_route contains an empty agent id.")
            .Concat(AgentIds
                .GroupBy(agentId => agentId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => $"internal_workflow_route duplicates {group.Key}."))
            .ToArray();
        return issues;
    }
}
