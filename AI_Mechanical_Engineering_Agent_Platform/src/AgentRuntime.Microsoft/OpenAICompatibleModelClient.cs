using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentRuntime.Microsoft;

public sealed class OpenAICompatibleModelClient : IRuntimeModelClient
{
    private readonly HttpClient _httpClient;

    public OpenAICompatibleModelClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateOwnedHttpClient(TimeSpan.FromSeconds(RuntimeConfiguration.DefaultTimeoutSeconds));
    }

    public OpenAICompatibleModelClient(RuntimeConfiguration configuration)
        : this(configuration, null)
    {
    }

    public OpenAICompatibleModelClient(RuntimeConfiguration configuration, HttpClient? httpClient = null)
        : this(TimeSpan.FromSeconds(RuntimeConfiguration.NormalizeTimeoutSeconds(configuration.TimeoutSeconds)), httpClient)
    {
    }

    public OpenAICompatibleModelClient(TimeSpan timeout)
        : this(timeout, null)
    {
    }

    public OpenAICompatibleModelClient(TimeSpan timeout, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateOwnedHttpClient(timeout);
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(RuntimeConfiguration.NormalizeTimeoutSeconds(configuration.TimeoutSeconds)));
        var effectiveCancellationToken = timeoutCts.Token;

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

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, effectiveCancellationToken);
            EnsureProviderSuccess(response);

            await using var stream = await response.Content.ReadAsStreamAsync(effectiveCancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: effectiveCancellationToken);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                throw new RuntimeProviderException(
                    "invalid_provider_response",
                    "Provider response did not contain a non-empty choices array.");
            }

            if (!choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
            {
                throw new RuntimeProviderException(
                    "invalid_provider_response",
                    "Provider response did not contain choices[0].message.content.");
            }

            return content.GetString() ?? string.Empty;
        }
        catch (RuntimeProviderException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RuntimeProviderException(
                "timeout",
                $"Provider request exceeded configured timeout of {RuntimeConfiguration.NormalizeTimeoutSeconds(configuration.TimeoutSeconds)} seconds.",
                innerException: ex);
        }
        catch (JsonException ex)
        {
            throw new RuntimeProviderException(
                "invalid_provider_response",
                "Provider response was not valid OpenAI-compatible JSON.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new RuntimeProviderException(
                "network_error",
                "Provider request failed due to a network or transport error.",
                ex.StatusCode,
                ex);
        }
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

    private static HttpClient CreateOwnedHttpClient(TimeSpan timeout) =>
        new()
        {
            Timeout = timeout
        };

    private static void EnsureProviderSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = response.StatusCode;
        var issueType = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "auth_error",
            (HttpStatusCode)429 => "rate_limit",
            >= HttpStatusCode.InternalServerError => "provider_error",
            _ => "provider_error"
        };
        var message = issueType switch
        {
            "auth_error" => "Provider credential check failed.",
            "rate_limit" => "Provider rate limit was reached.",
            _ => $"Provider returned HTTP {(int)statusCode}."
        };

        throw new RuntimeProviderException(issueType, message, statusCode);
    }
}
