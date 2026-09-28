using DomainSchemas;

namespace PlatformCore;

/// <summary>执行与审批边界；调度策略由具体引擎实现，不要求调用方依赖顺序引擎。</summary>
public interface IWorkflowEngine
{
    Task<WorkflowExecutionResult> ExecuteAsync(IEnumerable<WorkflowStep> steps, WorkflowContext context,
        CancellationToken cancellationToken = default);

    bool TryGetPendingHumanApproval(string workflowId, out HumanApprovalRequest request);

    Task<WorkflowApprovalSubmissionResult> SubmitHumanApprovalAsync(WorkflowApprovalSubmission submission,
        CancellationToken cancellationToken = default);
}
