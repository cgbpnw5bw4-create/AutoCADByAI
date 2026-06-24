using AgentContracts;
using DomainSchemas;

namespace PlatformCore;

public sealed class PlaceholderAgent : IAgent
{
    public PlaceholderAgent(
        string id,
        string name,
        AgentRole role,
        string description,
        AgentVisibility visibility,
        string? nextRecommendedAgentId = null)
    {
        Id = id;
        Name = name;
        Role = role;
        Description = description;
        Visibility = visibility;
        NextRecommendedAgentId = nextRecommendedAgentId;
    }

    public string Id { get; }

    public string Name { get; }

    public AgentRole Role { get; }

    public string Description { get; }

    public AgentVisibility Visibility { get; }

    private string? NextRecommendedAgentId { get; }

    public Task<AgentOutput> ExecuteAsync(AgentContext context)
    {
        var logs = new[]
        {
            $"{Id} received task {context.TaskId}.",
            "Placeholder agent returned structured platform response without CAD execution."
        };

        var output = new AgentOutput(
            AgentOutputStatus.Completed,
            $"[{Name}] 已完成平台骨架阶段的占位处理。",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            logs,
            NextRecommendedAgentId);

        return Task.FromResult(output);
    }
}
