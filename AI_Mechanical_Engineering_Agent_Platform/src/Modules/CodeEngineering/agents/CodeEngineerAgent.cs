using AgentContracts;
using DomainSchemas;

namespace PlatformCore.Modules.CodeEngineering.Agents;

public sealed class CodeEngineerAgent : IAgent
{
    public string Id => "code-engineer";

    public string Name => "代码工程师";

    public AgentRole Role { get; } = new(
        "code-engineer",
        "代码工程师",
        "CAD API / SDK 自动化代码开发规划");

    public string Description => "Internal CAD API, SDK and automation code planning agent.";

    public AgentVisibility Visibility => AgentVisibility.Internal;

    public Task<AgentOutput> ExecuteAsync(AgentContext context) =>
        Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            "Code engineering placeholder completed: automation code planning is available for future modules.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[] { "No real CAD API, SDK, COM or source generation was executed." },
            "code-reviewer"));
}
