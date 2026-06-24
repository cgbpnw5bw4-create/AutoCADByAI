namespace AgentContracts;

public enum AgentVisibility
{
    Public,
    Internal,
    Protected
}

public sealed record AgentRole(
    string Id,
    string DisplayName,
    string Description);
