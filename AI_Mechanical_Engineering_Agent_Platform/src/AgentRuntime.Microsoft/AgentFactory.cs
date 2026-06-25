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

    public IAgent CreateRuntimeAwareAgent(
        IAgent platformAgent,
        AgentRegistry agentRegistry,
        RuntimeConfiguration configuration,
        IRuntimeModelClient? modelClient = null)
    {
        if (!string.Equals(platformAgent.Id, "chief-engineer", StringComparison.OrdinalIgnoreCase))
        {
            return platformAgent;
        }

        if (configuration.EffectiveMode != AgentRuntimeMode.Microsoft)
        {
            if (configuration.FallbackUsed)
            {
                _auditLog.Record("agent-runtime", platformAgent.Id, "runtime_fallback_to_mock", configuration.FallbackReason ?? "Runtime fallback used.");
            }

            return platformAgent;
        }

        var internalAgentIds = agentRegistry.GetInternalAgents().Select(agent => agent.Id).ToArray();
        var client = modelClient ?? new RuntimeModelClientFactory().Create(configuration);
        var invoker = new MicrosoftRuntimeAgentInvoker(
            configuration,
            client,
            _auditLog,
            internalAgentIds);

        return new MicrosoftAgentAdapter(
            platformAgent,
            RuntimeAgentManifest.Create(platformAgent.Id, platformAgent.Visibility),
            AgentRuntimeMode.Microsoft,
            _auditLog,
            invoker);
    }
}
