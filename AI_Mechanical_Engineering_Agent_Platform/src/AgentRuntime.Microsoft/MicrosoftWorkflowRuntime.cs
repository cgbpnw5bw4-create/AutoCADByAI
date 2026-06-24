using PlatformCore;

namespace AgentRuntime.Microsoft;

public sealed class MicrosoftWorkflowRuntime
{
    private readonly SequentialWorkflowEngine _workflowEngine;

    public MicrosoftWorkflowRuntime(SequentialWorkflowEngine workflowEngine)
    {
        _workflowEngine = workflowEngine;
    }

    public string RuntimeName => "Microsoft Agent Framework Adapter Placeholder";

    public Task<WorkflowExecutionResult> ExecuteAsync(
        IEnumerable<WorkflowStep> steps,
        WorkflowContext context) =>
        _workflowEngine.ExecuteAsync(steps, context);
}
