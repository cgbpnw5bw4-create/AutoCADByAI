namespace AgentRuntime.Microsoft;

public sealed class MicrosoftAgentFrameworkModelClient : IRuntimeModelClient
{
    private readonly OpenAICompatibleModelClient _fallbackClient;

    public MicrosoftAgentFrameworkModelClient(OpenAICompatibleModelClient? fallbackClient = null)
    {
        _fallbackClient = fallbackClient ?? new OpenAICompatibleModelClient();
    }

    public MicrosoftAgentFrameworkModelClient(RuntimeConfiguration configuration)
    {
        _fallbackClient = new OpenAICompatibleModelClient(configuration);
    }

    public Task<string> GenerateTextAsync(
        string systemPrompt,
        string userMessage,
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        // V0.7 keeps the Microsoft Agent Framework dependency isolated in this project.
        // The provider call is intentionally delegated to the OpenAI-compatible path until
        // the concrete Agent Framework provider API is pinned for this platform.
        // TODO(runtime-provider): Replace this delegation when the concrete Agent Framework provider contract is selected.
        return _fallbackClient.GenerateTextAsync(systemPrompt, userMessage, configuration, cancellationToken);
    }
}
