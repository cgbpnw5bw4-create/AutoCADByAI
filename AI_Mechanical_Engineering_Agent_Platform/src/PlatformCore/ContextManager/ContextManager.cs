namespace PlatformCore;

public sealed class ContextManager
{
    public WorkflowContext CreateWorkflowContext(string taskId) =>
        new(taskId, new Dictionary<string, object?>());
}
