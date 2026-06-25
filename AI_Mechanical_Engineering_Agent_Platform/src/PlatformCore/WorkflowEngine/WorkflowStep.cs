namespace PlatformCore;

public sealed record WorkflowStep(
    string Name,
    Func<WorkflowContext, Task<WorkflowStepResult>> ExecuteAsync,
    string? StepId = null,
    string? NextStepId = null,
    int? MaxRetries = null);
