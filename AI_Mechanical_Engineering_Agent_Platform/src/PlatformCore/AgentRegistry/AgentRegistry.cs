using AgentContracts;

namespace PlatformCore;

public sealed class AgentRegistry
{
    private readonly Dictionary<string, IAgent> _agents = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IAgent agent)
    {
        _agents[agent.Id] = agent;
    }

    public IAgent? GetById(string id) =>
        _agents.TryGetValue(id, out var agent) ? agent : null;

    public IReadOnlyList<IAgent> GetAll() =>
        _agents.Values.OrderBy(agent => agent.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<IAgent> GetPublicAgents() =>
        GetAll().Where(agent => agent.Visibility == AgentVisibility.Public).ToArray();

    public IReadOnlyList<IAgent> GetInternalAgents() =>
        GetAll().Where(agent => agent.Visibility == AgentVisibility.Internal).ToArray();
}
