namespace PlatformCore;

public sealed record WorkflowExecutionResult(
    string TaskId,
    string FinalStatus,
    IReadOnlyList<WorkflowStepResult> Steps);
