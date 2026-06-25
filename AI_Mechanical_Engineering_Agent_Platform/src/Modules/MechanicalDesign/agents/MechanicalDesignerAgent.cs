using AgentContracts;
using DomainSchemas;

namespace PlatformCore.Modules.MechanicalDesign.Agents;

public sealed class MechanicalDesignerAgent : IAgent
{
    public string Id => "mechanical-designer";

    public string Name => "机械设计师";

    public AgentRole Role { get; } = new(
        "mechanical-designer",
        "机械设计师",
        "结构方案、机械合理性、参数建议");

    public string Description => "Internal mechanical design planner.";

    public AgentVisibility Visibility => AgentVisibility.Internal;

    public Task<AgentOutput> ExecuteAsync(AgentContext context)
    {
        if (IsScenario(context, "mechanical_retry_then_passed") && Attempt(context) == 1)
        {
            return Task.FromResult(new AgentOutput(
                AgentOutputStatus.Completed,
                "Mechanical design review found a retryable planning issue on first attempt.",
                Array.Empty<ArtifactInfo>(),
                new[] { "retryable issue: initial mechanical parameter assumption needs revision" },
                new[] { "MechanicalDesignerAgent simulated first-attempt rejection." },
                "cad-modeler"));
        }

        return Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            "Mechanical design review completed: structure assumptions are reasonable for V0.6 planning.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[]
            {
                "Checked structure feasibility at planning level.",
                "Captured parameter and risk notes without calling CAD tools."
            },
            "cad-modeler"));
    }

    private static bool IsScenario(AgentContext context, string scenario) =>
        context.Input.Context.TryGetValue("test_scenario", out var value) &&
        string.Equals(value, scenario, StringComparison.OrdinalIgnoreCase);

    private static int Attempt(AgentContext context) =>
        context.SharedState.TryGetValue("internal_agent_attempt", out var value) && value is int attempt
            ? attempt
            : 1;
}
