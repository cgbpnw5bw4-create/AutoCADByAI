using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using AgentContracts;
using DomainSchemas;
using QualityGate;

namespace PlatformCore;

public sealed record AgentTaskResult(
    PlatformTask Task,
    string AgentId,
    string AgentName,
    AgentOutput? Output = null,
    GateDecision? GateDecision = null,
    RejectReport? RejectReport = null,
    HumanApprovalRequest? PendingApproval = null,
    string? FailureStage = null);

public sealed record AgentTaskCreation(AgentTaskResult Result, string AccessToken);

public sealed record AgentTaskApprovalResult(
    bool Accepted, AgentTaskResult? Result, string? FailureReason = null, bool NotFound = false);

// 单进程任务协调器。保存原始 Agent 实例和输入；审批不能替换任务内容。
public sealed class AgentTaskService
{
    private readonly PlatformKernel _platform;
    private readonly IGatekeeper _gatekeeper = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder());
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public AgentTaskService(PlatformKernel platform) => _platform = platform;

    public async Task<AgentTaskCreation?> ExecuteAsync(string agentId, AgentInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Attachments);
        ArgumentNullException.ThrowIfNull(input.Context);
        var agent = _platform.AgentRegistry.GetById(agentId);
        if (agent is null || !_platform.PermissionManager.CanExposeToExternalGateway(agent)) return null;
        var task = _platform.TaskStore.Create($"Gateway message from {input.Source}");
        var context = new AgentContext(task.Id, input with
        {
            Attachments = Array.AsReadOnly(input.Attachments.ToArray()),
            Context = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(input.Context))
        }, new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>()), DateTimeOffset.UtcNow);
        var accessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new Session(agent, context, SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)), new(task, agent.Id, agent.Name));
        _sessions[task.Id] = session;
        SetRunning(session);
        try
        {
            // 接受后继续收尾，不因 HTTP 断开重跑已经开始的业务动作。
            _platform.AuditLog.Record("agent", agent.Id, "public_agent_invoked", $"Task {task.Id} invoked {agent.Id}.");
            Complete(session, await agent.ExecuteAsync(context));
        }
        catch (Exception ex) { CompleteException(session, ex); }
        return new(session.Snapshot, accessToken);
    }

    public AgentTaskResult? Get(string taskId, string? accessToken) =>
        TryAuthorize(taskId, accessToken, out var session) ? session.Snapshot : null;

    public async Task<AgentTaskApprovalResult> SubmitApprovalAsync(
        string taskId, string? accessToken, WorkflowApprovalSubmission submission, CancellationToken cancellationToken = default)
    {
        if (!TryAuthorize(taskId, accessToken, out var session)) return new(false, null, "task_not_found", true);
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.ApprovalLock.Wait(0)) return new(false, session.Snapshot, "task_approval_in_progress");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = session.Snapshot;
            var pending = before.PendingApproval;
            if (before.Task.Status != PlatformTaskStatus.WaitingForHumanApproval || pending is null)
                return new(false, before, "task_approval_not_pending");
            if (string.IsNullOrWhiteSpace(submission.ApprovalRequestId) || string.IsNullOrWhiteSpace(submission.StepId) ||
                string.IsNullOrWhiteSpace(submission.SubmittedBy) || !Enum.IsDefined(submission.Decision))
                return new(false, before, "workflow_approval_invalid_submission");
            if (!string.Equals(pending.WorkflowId, submission.WorkflowId, StringComparison.Ordinal) ||
                !string.Equals(pending.StepId, submission.StepId, StringComparison.Ordinal) ||
                !string.Equals(pending.ApprovalRequestId, submission.ApprovalRequestId, StringComparison.Ordinal))
                return new(false, before, "workflow_approval_identity_mismatch");
            if (session.Agent is not IHumanApprovalAgent resumable)
                return new(false, before, "task_approval_resume_unsupported");

            SetRunning(session);
            try
            {
                var result = await resumable.ResumeHumanApprovalAsync(session.Context, submission, CancellationToken.None);
                if (!result.Accepted)
                {
                    _platform.TaskStore.UpdateStatus(taskId, before.Task.Status);
                    session.Snapshot = before with { Task = _platform.TaskStore.Get(taskId)! };
                    return new(false, session.Snapshot, result.FailureReason);
                }
                Complete(session, result.Output ?? throw new InvalidOperationException("task_approval_output_missing"));
                return new(true, session.Snapshot);
            }
            catch (Exception ex)
            {
                CompleteException(session, ex);
                return new(true, session.Snapshot);
            }
        }
        finally { session.ApprovalLock.Release(); }
    }

    private void SetRunning(Session session)
    {
        _platform.TaskStore.UpdateStatus(session.Context.TaskId, PlatformTaskStatus.Running);
        session.Snapshot = new(_platform.TaskStore.Get(session.Context.TaskId)!, session.Agent.Id, session.Agent.Name);
        _platform.AuditLog.Record("task", session.Context.TaskId, "task_running", "Task execution started or resumed.");
    }

    private void Complete(Session session, AgentOutput output, string? failureStage = null, bool cancelled = false)
    {
        session.RuntimeMetadata = output.RuntimeMetadata ?? session.RuntimeMetadata;
        var evaluation = _gatekeeper.Evaluate(AgentOutputReviewMapper.ToReviewReport($"gateway-quality-gate:{session.Agent.Id}", output));
        var pending = output.InternalCollaborationReport?.HumanApprovalRequest;
        if (evaluation.Decision.Result == GateDecisionResult.NeedsHumanApproval &&
            (pending is null || string.IsNullOrWhiteSpace(pending.ApprovalRequestId) || session.Agent is not IHumanApprovalAgent))
        {
            Complete(session, FailureOutput("task_approval_continuation_missing", "等待审批的输出没有可恢复的具体审批请求。")
                with { RuntimeMetadata = session.RuntimeMetadata }, "task_approval_continuation_missing");
            return;
        }
        var status = evaluation.Decision.Result switch
        {
            GateDecisionResult.Passed => PlatformTaskStatus.Passed,
            GateDecisionResult.Rejected => PlatformTaskStatus.Rejected,
            GateDecisionResult.NeedsHumanApproval => PlatformTaskStatus.WaitingForHumanApproval,
            _ => cancelled ? PlatformTaskStatus.Cancelled : PlatformTaskStatus.Failed
        };
        _platform.TaskStore.UpdateStatus(session.Context.TaskId, status);
        failureStage ??= status switch
        {
            PlatformTaskStatus.Failed => "task_quality_gate_failed",
            PlatformTaskStatus.Rejected => "task_quality_gate_rejected",
            _ => null
        };
        session.Snapshot = new(_platform.TaskStore.Get(session.Context.TaskId)!, session.Agent.Id, session.Agent.Name,
            output, evaluation.Decision, evaluation.RejectReport,
            status == PlatformTaskStatus.WaitingForHumanApproval ? pending : null, failureStage);
        _platform.AuditLog.Record("quality-gate", "AgentGatewayHost", "quality_gate_evaluated", evaluation.Decision.Reason);
        _platform.AuditLog.Record("task", session.Context.TaskId, "task_status_changed", $"Task status is {status}.");
    }

    private void CompleteException(Session session, Exception exception)
    {
        var stage = exception is OperationCanceledException ? "task_execution_cancelled" : "task_execution_exception";
        var reason = $"{exception.GetType().Name}: {exception.Message}";
        _platform.AuditLog.Record("task", session.Context.TaskId, stage, reason);
        Complete(session, FailureOutput(stage, reason) with { RuntimeMetadata = session.RuntimeMetadata }, stage, exception is OperationCanceledException);
    }

    private static AgentOutput FailureOutput(string stage, string reason) => new(AgentOutputStatus.Failed,
        $"任务已停止：{reason}", [], [$"{stage}: {reason}。请查询任务记录并修正原因；不能重放已接受的审批。"], [], "error-diagnosis");

    private bool TryAuthorize(string taskId, string? accessToken, out Session session)
    {
        session = null!;
        if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(accessToken) || accessToken.Length != 64 ||
            !_sessions.TryGetValue(taskId, out var found)) return false;
        if (!CryptographicOperations.FixedTimeEquals(found.TokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)))) return false;
        session = found;
        return true;
    }

    private sealed class Session(IAgent agent, AgentContext context, byte[] tokenHash, AgentTaskResult snapshot)
    {
        public IAgent Agent { get; } = agent;
        public AgentContext Context { get; } = context;
        public byte[] TokenHash { get; } = tokenHash;
        public RuntimeMetadata? RuntimeMetadata { get; set; }
        public SemaphoreSlim ApprovalLock { get; } = new(1, 1);
        public volatile AgentTaskResult Snapshot = snapshot;
    }
}
