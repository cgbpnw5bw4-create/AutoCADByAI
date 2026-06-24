using AgentContracts;
using PlatformCore;

namespace AgentRuntime.Microsoft;

public sealed class AgentFactory
{
    private readonly InMemoryAuditLog _auditLog;

    public AgentFactory()
        : this(new InMemoryAuditLog())
    {
    }

    public AgentFactory(InMemoryAuditLog auditLog)
    {
        _auditLog = auditLog;
    }

    public IAgent CreatePlatformAgentAdapter(IAgent platformAgent, object? microsoftAgentInstance = null) =>
        new MicrosoftAgentAdapter(platformAgent, microsoftAgentInstance);

    public IAgent CreateMockAgent(string agentId) =>
        new MicrosoftAgentAdapter(
            RuntimeAgentManifest.Create(agentId),
            AgentRuntimeMode.Mock,
            _auditLog);

    public IAgent CreateMicrosoftRuntimeAgent(
        RuntimeAgentManifest manifest,
        IMicrosoftRuntimeAgentInvoker? invoker = null) =>
        new MicrosoftAgentAdapter(
            manifest,
            AgentRuntimeMode.Microsoft,
            _auditLog,
            invoker);
}
