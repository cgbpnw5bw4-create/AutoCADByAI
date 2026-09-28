using ModelRuntime;

namespace AgentRuntime.Microsoft;

/// <summary>将既有客户端与环境配置限制在兼容适配层，业务合同无需知道提供商或模型名。</summary>
public sealed class ConfiguredModelProvider(
    IRuntimeModelClient client,
    RuntimeConfiguration configuration) : IModelProvider
{
    public Task<string> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
        client.GenerateTextAsync(request.SystemPrompt, request.UserMessage, configuration, cancellationToken);
}
