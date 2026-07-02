using AgentContracts;
using DomainSchemas;

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
        AgentOutput output;
        try
        {
            output = await agent.ExecuteAsync(context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var rootException = ex.GetBaseException();
            output = new AgentOutput(
                AgentOutputStatus.Failed,
                $"Internal agent '{agent.Id}' failed with an exception.",
                Array.Empty<ArtifactInfo>(),
                new[] { $"internal_agent_exception: {rootException.GetType().Name}: {rootException.Message}" },
                new[]
                {
                    $"internal_agent_exception: agent_id={agent.Id}, exception_type={rootException.GetType().FullName}, message={rootException.Message}"
                },
                "error-diagnosis");
            _auditLog.Record("agent", agent.Id, "internal_agent_failed", $"Internal agent '{agent.Id}' failed with exception {rootException.GetType().Name}.");
            return output;
        }

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
