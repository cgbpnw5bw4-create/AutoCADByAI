using AgentContracts;
using DomainSchemas;

namespace PlatformCore.Modules.CodeReview.Agents;

public sealed class CodeReviewerAgent : IAgent
{
    public string Id => "code-reviewer";

    public string Name => "代码复审工程师";

    public AgentRole Role { get; } = new(
        "code-reviewer",
        "代码复审工程师",
        "代码安全性、稳定性、可维护性审查");

    public string Description => "Internal automation code review planning agent.";

    public AgentVisibility Visibility => AgentVisibility.Internal;

    public Task<AgentOutput> ExecuteAsync(AgentContext context) =>
        Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            "Code review placeholder completed: safety, stability and maintainability review boundary is available.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[] { "No real source code review or runtime execution was performed." },
            null));
}
