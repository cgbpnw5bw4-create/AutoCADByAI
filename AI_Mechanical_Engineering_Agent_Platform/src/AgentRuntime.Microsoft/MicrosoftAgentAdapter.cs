using AgentContracts;

namespace AgentRuntime.Microsoft;

public sealed class MicrosoftAgentAdapter : IAgent
{
    private readonly IAgent _platformAgent;

    public MicrosoftAgentAdapter(IAgent platformAgent, object? microsoftAgentInstance = null)
    {
        _platformAgent = platformAgent;
        MicrosoftAgentInstance = microsoftAgentInstance;
    }

    public object? MicrosoftAgentInstance { get; }

    public string Id => _platformAgent.Id;

    public string Name => _platformAgent.Name;

    public AgentRole Role => _platformAgent.Role;

    public string Description => _platformAgent.Description;

    public AgentVisibility Visibility => _platformAgent.Visibility;

    public Task<AgentOutput> ExecuteAsync(AgentContext context) =>
        _platformAgent.ExecuteAsync(context);
}
