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

        if (IsSolidWorksPlanningRequest(context))
        {
            return Task.FromResult(new AgentOutput(
                AgentOutputStatus.Completed,
                "SolidWorks modeling intent detected. Recommended next action is to generate a SolidWorksBuildPlan through SolidWorksBuildPlanSkill.",
                Array.Empty<ArtifactInfo>(),
                Array.Empty<string>(),
                new[]
                {
                    "CadModelerAgent only proposes the SolidWorksBuildPlan handoff.",
                    "CadModelerAgent does not directly call SolidWorksWorker.",
                    "CadModelerAgent does not directly call FakeSolidWorksWorker.",
                    "No SolidWorks, COM, SldWorks.Application or Worker call was executed."
                },
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

    private static bool IsSolidWorksPlanningRequest(AgentContext context)
    {
        var message = context.Input.Message;
        var keywords = new[] { "SolidWorks", "solidworks", "SW", "板件", "plate", "建模" };
        return keywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
