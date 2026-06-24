using AgentContracts;
using PlatformCore;

namespace AgentGatewayHost;

public sealed class AgentMessageDispatcher
{
    private readonly PlatformKernel _platform;

    public AgentMessageDispatcher(PlatformKernel platform)
    {
        _platform = platform;
    }

    public async Task<GatewayMessageResponse?> DispatchAsync(string agentId, GatewayMessageRequest request)
    {
        var agent = _platform.AgentRegistry.GetById(agentId);
        if (agent is null || !_platform.PermissionManager.CanExposeToExternalGateway(agent))
        {
            return null;
        }

        var task = _platform.TaskStore.Create($"Gateway message from {request.Source}");
        var input = new AgentInput(
            request.Source,
            request.Channel,
            request.ConversationId,
            request.User,
            request.Message,
            request.Attachments,
            request.Context);

        var context = new AgentContext(
            task.Id,
            input,
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

        var output = await agent.ExecuteAsync(context);
        _platform.AuditLog.Record("agent", agent.Id, "gateway-message", output.Message);

        return new GatewayMessageResponse(
            agent.Id,
            agent.Name,
            output.Status.ToString().ToLowerInvariant(),
            output.Message,
            output.Artifacts,
            output.Issues,
            output.NextRecommendedAgentId);
    }
}
