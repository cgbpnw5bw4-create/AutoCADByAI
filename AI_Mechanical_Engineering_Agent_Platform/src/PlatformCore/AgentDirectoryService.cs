namespace PlatformCore;

public sealed record AgentDirectoryEntry(
    string Id,
    string DisplayName,
    string Role,
    string Mention,
    string Visibility);

public sealed class AgentDirectoryService
{
    private readonly AgentRegistry _agentRegistry;
    private readonly PermissionManager _permissionManager;

    public AgentDirectoryService(AgentRegistry agentRegistry)
        : this(agentRegistry, new PermissionManager())
    {
    }

    public AgentDirectoryService(AgentRegistry agentRegistry, PermissionManager permissionManager)
    {
        _agentRegistry = agentRegistry;
        _permissionManager = permissionManager;
    }

    public IReadOnlyList<AgentDirectoryEntry> GetVisibleAgents() =>
        _agentRegistry.GetAll()
            .Where(_permissionManager.CanExposeToExternalGateway)
            .Select(agent => new AgentDirectoryEntry(
                agent.Id,
                agent.Name,
                agent.Role.Description,
                $"@{agent.Name}",
                agent.Visibility.ToString()))
            .ToArray();
}
