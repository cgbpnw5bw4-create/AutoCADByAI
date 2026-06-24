using AgentContracts;
using DomainSchemas;
using PlatformCore;
using QualityGate;

namespace AgentGatewayHost;

public sealed class AgentMessageDispatcher
{
    private readonly PlatformKernel _platform;
    private readonly IGatekeeper _gatekeeper;

    public AgentMessageDispatcher(PlatformKernel platform)
    {
        _platform = platform;
        _gatekeeper = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder());
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
        var reviewReport = AgentOutputReviewMapper.ToReviewReport($"gateway-quality-gate:{agent.Id}", output);
        var gateEvaluation = _gatekeeper.Evaluate(reviewReport);
        _platform.AuditLog.Record("quality-gate", "AgentGatewayHost", gateEvaluation.Decision.Result.ToString(), gateEvaluation.Decision.Reason);

        var responseStatus = gateEvaluation.Decision.Result switch
        {
            GateDecisionResult.Passed => output.Status.ToString().ToLowerInvariant(),
            GateDecisionResult.Rejected => "rejected",
            GateDecisionResult.Failed => "failed",
            GateDecisionResult.NeedsHumanApproval => "needs_human_approval",
            _ => "failed"
        };

        var responseMessage = gateEvaluation.Decision.Result == GateDecisionResult.Passed
            ? output.Message
            : gateEvaluation.RejectReport?.Message ?? gateEvaluation.Decision.Reason;

        var issues = gateEvaluation.RejectReport?.Reasons ?? output.Issues;

        return new GatewayMessageResponse(
            agent.Id,
            agent.Name,
            responseStatus,
            responseMessage,
            output.Artifacts,
            issues,
            gateEvaluation.Decision,
            gateEvaluation.RejectReport,
            output.NextRecommendedAgentId);
    }

}
