using AgentContracts;
using DomainSchemas;

namespace PlatformCore.Modules.DrawingGeneration.Agents;

public sealed class DrawingEngineerAgent : IAgent
{
    public string Id => "drawing-engineer";

    public string Name => "工程图工程师";

    public AgentRole Role { get; } = new(
        "drawing-engineer",
        "工程图工程师",
        "工程图生成规划");

    public string Description => "Internal drawing generation planner.";

    public AgentVisibility Visibility => AgentVisibility.Internal;

    public Task<AgentOutput> ExecuteAsync(AgentContext context)
    {
        if (IsScenario(context, "drawing_engineer_failed"))
        {
            return Task.FromResult(new AgentOutput(
                AgentOutputStatus.Failed,
                "Drawing generation planning failed in simulated V0.6 workflow.",
                Array.Empty<ArtifactInfo>(),
                new[] { "critical issue: drawing generation planning cannot continue" },
                new[] { "DrawingEngineerAgent simulated a failed step." },
                "error-diagnosis"));
        }

        return Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            "Drawing generation plan completed: DrawingSpec placeholder is ready for review.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[]
            {
                "Prepared required views and drawing planning notes.",
                "No drawing file was generated in V0.6."
            },
            "drawing-reviewer"));
    }

    private static bool IsScenario(AgentContext context, string scenario) =>
        context.Input.Context.TryGetValue("test_scenario", out var value) &&
        string.Equals(value, scenario, StringComparison.OrdinalIgnoreCase);
}
