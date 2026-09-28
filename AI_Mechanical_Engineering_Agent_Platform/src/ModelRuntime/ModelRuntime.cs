namespace ModelRuntime;

/// <summary>模型接入边界。输出仍是待校验的建议，不能授予 Worker 或 API 执行权限。</summary>
public sealed class ModelRuntime(IModelProvider provider)
{
    private readonly IModelProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public async Task<string> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Purpose)) throw new ArgumentOutOfRangeException(nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserMessage);
        cancellationToken.ThrowIfCancellationRequested();
        var response = await _provider.GenerateAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidOperationException("model_response_empty: 模型未返回工程建议。");
        return response;
    }
}
