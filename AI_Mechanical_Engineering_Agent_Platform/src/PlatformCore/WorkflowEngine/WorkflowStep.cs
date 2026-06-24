namespace PlatformCore;

public sealed record WorkflowStep(
    string Name,
    Func<WorkflowContext, Task<WorkflowStepResult>> ExecuteAsync);
