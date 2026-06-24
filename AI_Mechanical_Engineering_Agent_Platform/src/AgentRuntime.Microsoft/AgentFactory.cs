using AgentContracts;

namespace AgentRuntime.Microsoft;

public sealed class AgentFactory
{
    public IAgent CreatePlatformAgentAdapter(IAgent platformAgent, object? microsoftAgentInstance = null) =>
        new MicrosoftAgentAdapter(platformAgent, microsoftAgentInstance);
}
