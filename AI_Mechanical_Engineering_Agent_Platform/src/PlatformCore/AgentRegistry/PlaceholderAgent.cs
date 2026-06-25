using AgentContracts;
using DomainSchemas;
using PlatformCore.Modules.RequirementUnderstanding.Agents;

namespace PlatformCore;

public sealed class PlaceholderAgent : IAgent
{
    private readonly ChiefEngineerOrchestrator? _chiefEngineerOrchestrator;

    public PlaceholderAgent(
        string id,
        string name,
        AgentRole role,
        string description,
        AgentVisibility visibility,
        string? nextRecommendedAgentId = null,
        ChiefEngineerOrchestrator? chiefEngineerOrchestrator = null)
    {
        Id = id;
        Name = name;
        Role = role;
        Description = description;
        Visibility = visibility;
        NextRecommendedAgentId = nextRecommendedAgentId;
        _chiefEngineerOrchestrator = chiefEngineerOrchestrator;
    }

    public string Id { get; }

    public string Name { get; }

    public AgentRole Role { get; }

    public string Description { get; }

    public AgentVisibility Visibility { get; }

    private string? NextRecommendedAgentId { get; }

    public Task<AgentOutput> ExecuteAsync(AgentContext context)
    {
        if (Id == "chief-engineer" && _chiefEngineerOrchestrator is not null)
        {
            return _chiefEngineerOrchestrator.ExecuteAsync(context, Id, Name);
        }

        return Task.FromResult(GenericOutput(context));
    }

    private AgentOutput GenericOutput(AgentContext context) =>
        Completed(
            $"{Id} received task {context.TaskId} and returned fallback placeholder output.",
            ["Fallback PlaceholderAgent returned structured platform response without CAD execution."]);

    private AgentOutput Completed(string message, IReadOnlyList<string> logs) =>
        new(
            AgentOutputStatus.Completed,
            message,
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            logs,
            NextRecommendedAgentId);
}
