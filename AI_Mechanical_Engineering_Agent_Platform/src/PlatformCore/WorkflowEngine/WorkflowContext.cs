namespace PlatformCore;

public sealed record WorkflowContext(
    string TaskId,
    IDictionary<string, object?> Items);
