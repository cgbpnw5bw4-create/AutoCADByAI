using AgentContracts;

namespace PlatformCore;

public sealed class InternalAgentRouter
{
    private readonly AgentRegistry _agentRegistry;
    private readonly InMemoryAuditLog _auditLog;

    public InternalAgentRouter(AgentRegistry agentRegistry, InMemoryAuditLog auditLog)
    {
        _agentRegistry = agentRegistry;
        _auditLog = auditLog;
    }

    public async Task<AgentOutput> InvokeInternalAgentAsync(string agentId, AgentContext context)
    {
        var agent = _agentRegistry.GetById(agentId)
            ?? throw new InvalidOperationException($"Internal agent '{agentId}' was not found.");

        if (agent.Visibility != AgentVisibility.Internal)
        {
            throw new InvalidOperationException($"Agent '{agentId}' is not an Internal agent.");
        }

        _auditLog.Record("agent", agent.Id, "internal_agent_invoked", $"Internal agent '{agent.Id}' invoked for task {context.TaskId}.");
        var output = await agent.ExecuteAsync(context);
        _auditLog.Record("agent", agent.Id, "internal_agent_completed", $"Internal agent '{agent.Id}' completed with status {output.Status}.");
        return output;
    }

    public async Task<IReadOnlyList<AgentOutput>> InvokeInternalAgentsSequentiallyAsync(
        IEnumerable<string> agentIds,
        AgentContext context)
    {
        var outputs = new List<AgentOutput>();
        foreach (var agentId in agentIds)
        {
            outputs.Add(await InvokeInternalAgentAsync(agentId, context));
        }

        return outputs;
    }
}
