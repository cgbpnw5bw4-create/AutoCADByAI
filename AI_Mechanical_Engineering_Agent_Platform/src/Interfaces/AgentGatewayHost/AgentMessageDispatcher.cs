using AgentContracts;
using PlatformCore;
using DomainSchemas;

namespace AgentGatewayHost;

public sealed class AgentMessageDispatcher
{
    private readonly PlatformKernel _platform;
    private readonly AgentTaskService _tasks;

    public AgentMessageDispatcher(PlatformKernel platform)
    {
        _platform = platform;
        _tasks = new AgentTaskService(platform);
    }

    public async Task<GatewayMessageResponse?> DispatchAsync(string agentId, GatewayMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Source) || string.IsNullOrWhiteSpace(request.Channel) ||
            string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.User) ||
            string.IsNullOrWhiteSpace(request.Message) || request.Attachments is null || request.Context is null)
            throw new ArgumentException("gateway_invalid_request: 请求来源、渠道、对话、用户、消息和集合字段均为必填。");
        _platform.AuditLog.Record("gateway", agentId, "gateway_request_received", $"Gateway request received from {request.Source}.");
        var creation = await _tasks.ExecuteAsync(agentId,
            new AgentInput(request.Source, request.Channel, request.ConversationId, request.User, request.Message,
                request.Attachments, request.Context), cancellationToken);
        if (creation is null)
        {
            _platform.AuditLog.Record("gateway", agentId, "gateway_request_rejected", "Gateway rejected non-public or unknown agent.");
            return null;
        }
        var response = MapResponse(creation.Result) with { TaskAccessToken = creation.AccessToken };
        _platform.AuditLog.Record("gateway", agentId, "gateway_response_returned", $"Task {response.TaskId} returned {response.Status}.");
        return response;
    }

    public GatewayTaskResponse? GetTask(string taskId, string? accessToken)
    {
        var result = _tasks.Get(taskId, accessToken);
        return result is null ? null : MapTask(result);
    }

    public async Task<GatewayApprovalResponse> SubmitApprovalAsync(string taskId, string? accessToken,
        GatewayApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _tasks.SubmitApprovalAsync(taskId, accessToken,
            new(request.WorkflowId, request.Decision ?? (WorkflowApprovalDecision)(-1), request.SubmittedBy, request.Comment,
                ApprovalRequestId: request.ApprovalRequestId, StepId: request.StepId), cancellationToken);
        return new(result.Accepted, result.Result is null ? null : MapTask(result.Result), result.FailureReason, result.NotFound);
    }

    private static GatewayTaskResponse MapTask(AgentTaskResult result) =>
        new(result.Task, result.PendingApproval, result.FailureStage, result.Output is null ? null : MapResponse(result));

    private static GatewayMessageResponse MapResponse(AgentTaskResult result)
    {
        var output = result.Output!;
        var decision = result.GateDecision!;
        var metadata = output.RuntimeMetadata;
        var status = decision.Result switch
        {
            GateDecisionResult.Passed => output.Status.ToString().ToLowerInvariant(),
            GateDecisionResult.Rejected => "rejected",
            GateDecisionResult.NeedsHumanApproval => "needs_human_approval",
            _ => "failed"
        };
        return new(result.AgentId, result.AgentName, status,
            decision.Result == GateDecisionResult.Passed ? output.Message : result.RejectReport?.Message ?? decision.Reason,
            output.Artifacts, result.RejectReport?.Reasons ?? output.Issues, decision, result.RejectReport,
            output.InternalCollaborationReport, output.NextRecommendedAgentId,
            metadata?.RuntimeMode ?? "Mock", metadata?.RuntimeProvider, metadata?.RuntimeModel,
            metadata?.RuntimeFallbackUsed ?? false, metadata?.RuntimeFallbackReason, metadata?.ChiefEngineerRuntimeUsed ?? false,
            result.Task.Id, result.Task.Status, result.PendingApproval, FailureStage: result.FailureStage);
    }
}
