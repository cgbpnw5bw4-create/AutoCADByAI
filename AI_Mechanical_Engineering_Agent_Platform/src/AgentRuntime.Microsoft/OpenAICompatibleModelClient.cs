using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentRuntime.Microsoft;

public sealed class OpenAICompatibleModelClient : IRuntimeModelClient
{
    private readonly HttpClient _httpClient;

    public OpenAICompatibleModelClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> GenerateTextAsync(
        string systemPrompt,
        string userMessage,
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (configuration.ApiKey is null)
        {
            throw new InvalidOperationException("Runtime credential is not configured.");
        }

        var endpoint = ResolveEndpoint(configuration);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);

        var payload = new
        {
            model = configuration.Model,
            temperature = configuration.Temperature,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var choices = document.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Model response did not contain choices.");
        }

        return choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private static Uri ResolveEndpoint(RuntimeConfiguration configuration)
    {
        var baseUrl = configuration.BaseUrl ?? "https://api.openai.com";
        var normalized = baseUrl.TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(normalized);
        }

        return new Uri($"{normalized}/v1/chat/completions");
    }
}
