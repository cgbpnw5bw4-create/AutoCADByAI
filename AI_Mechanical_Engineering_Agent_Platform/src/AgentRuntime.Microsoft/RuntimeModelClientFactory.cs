namespace AgentRuntime.Microsoft;

public sealed class RuntimeModelClientFactory
{
    public IRuntimeModelClient Create(RuntimeConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.Provider) &&
            configuration.Provider.Contains("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return new MicrosoftAgentFrameworkModelClient(configuration);
        }

        return new OpenAICompatibleModelClient(configuration);
    }
}
