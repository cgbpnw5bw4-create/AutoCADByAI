namespace AgentRuntime.Microsoft;

public interface IRuntimeModelClient
{
    Task<string> GenerateTextAsync(
        string systemPrompt,
        string userMessage,
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default);
}
