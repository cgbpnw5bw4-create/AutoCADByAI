namespace AgentContracts;

public sealed record AgentDecision(
    string AgentId,
    string Decision,
    string Reason,
    string? NextAgentId,
    DateTimeOffset CreatedAt);
