namespace AgentRuntime.Microsoft;

public enum AgentRuntimeMode
{
    Mock,
    Microsoft
}

public sealed record AgentRuntimeOptions(
    AgentRuntimeMode Mode = AgentRuntimeMode.Mock,
    string? ModelProvider = null,
    string? ModelName = null);
