using AgentContracts;
using DomainSchemas;

namespace PlatformCore.Modules.CADModeling.Agents;

public sealed class CadModelerAgent : IAgent
{
    public string Id => "cad-modeler";

    public string Name => "CAD建模工程师";

    public AgentRole Role { get; } = new(
        "cad-modeler",
        "CAD建模工程师",
        "建模规划、BuildSpec 生成、Worker 调用规划");

    public string Description => "Internal CAD modeling planner. Does not call CAD software directly.";

    public AgentVisibility Visibility => AgentVisibility.Internal;

    public Task<AgentOutput> ExecuteAsync(AgentContext context)
    {
        if (IsScenario(context, "cad_max_retries_exceeded"))
        {
            return Task.FromResult(new AgentOutput(
                AgentOutputStatus.Completed,
                "CAD modeling plan still has retryable BuildSpec issues.",
                Array.Empty<ArtifactInfo>(),
                new[] { "retryable issue: BuildSpec placeholder needs another planning pass" },
                new[] { "CadModelerAgent simulated persistent retryable rejection." },
                "drawing-engineer"));
        }

        return Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            "CAD modeling plan completed: BuildSpec placeholder is ready for future worker handoff.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[]
            {
                "Prepared BuildSpec planning notes.",
                "No SolidWorks, AutoCAD, API, SDK, COM or Worker call was executed."
            },
            "drawing-engineer"));
    }

    private static bool IsScenario(AgentContext context, string scenario) =>
        context.Input.Context.TryGetValue("test_scenario", out var value) &&
        string.Equals(value, scenario, StringComparison.OrdinalIgnoreCase);
}
