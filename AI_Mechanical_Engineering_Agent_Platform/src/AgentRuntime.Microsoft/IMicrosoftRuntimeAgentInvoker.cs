using AgentContracts;

namespace AgentRuntime.Microsoft;

public interface IMicrosoftRuntimeAgentInvoker
{
    Task<AgentOutput> InvokeAsync(RuntimeAgentManifest manifest, AgentContext context, CancellationToken cancellationToken = default);
}
