using PlatformCore;

namespace AgentRuntime.Microsoft;

public sealed class MicrosoftWorkflowRuntime
{
    private readonly SequentialWorkflowEngine _workflowEngine;

    public MicrosoftWorkflowRuntime(AgentRuntimeMode runtimeMode = AgentRuntimeMode.Mock)
        : this(new SequentialWorkflowEngine(), runtimeMode)
    {
    }

    public MicrosoftWorkflowRuntime(SequentialWorkflowEngine workflowEngine, AgentRuntimeMode runtimeMode = AgentRuntimeMode.Mock)
    {
        _workflowEngine = workflowEngine;
        RuntimeMode = runtimeMode;
    }

    public AgentRuntimeMode RuntimeMode { get; }

    public string RuntimeName => RuntimeMode == AgentRuntimeMode.Mock
        ? "Microsoft Agent Framework Mock Runtime"
        : "Microsoft Agent Framework Runtime Adapter";

    public Task<WorkflowExecutionResult> ExecuteAsync(
        IEnumerable<WorkflowStep> steps,
        WorkflowContext context) =>
        _workflowEngine.ExecuteAsync(steps, context);
}
