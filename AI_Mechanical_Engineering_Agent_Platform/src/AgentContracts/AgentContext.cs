namespace AgentContracts;

public sealed record AgentContext(
    string TaskId,
    AgentInput Input,
    IReadOnlyDictionary<string, object?> SharedState,
    DateTimeOffset CreatedAt);
