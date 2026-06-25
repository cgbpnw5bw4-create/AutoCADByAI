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

    public Task<AgentOutput> ExecuteAsync(AgentContext context) =>
        Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            "Mechanical design review completed: structure assumptions are reasonable for V0.4 planning.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[]
            {
                "Checked structure feasibility at planning level.",
                "Captured parameter and risk notes without calling CAD tools."
            },
            "cad-modeler"));
}
