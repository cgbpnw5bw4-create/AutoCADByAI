using AgentContracts;

namespace PlatformCore;

public interface IHumanApprovalAgent
{
    Task<AgentApprovalResult> ResumeHumanApprovalAsync(
        AgentContext context,
        WorkflowApprovalSubmission submission,
        CancellationToken cancellationToken = default);
}

public sealed record AgentApprovalResult(bool Accepted, AgentOutput? Output, string? FailureReason = null);
