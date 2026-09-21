using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Infrastructure.Llm;

public class OpenAiLlmProvider : ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiConfig _config;
    private readonly ILogger<OpenAiLlmProvider> _logger;

    public string ProviderName => "OpenAI";

    public OpenAiLlmProvider(HttpClient httpClient, OpenAiConfig config, ILogger<OpenAiLlmProvider> logger)
        : this(httpClient, config, logger, httpTimeoutSeconds: 60)
    {
    }

    public OpenAiLlmProvider(
        HttpClient httpClient, OpenAiConfig config, ILogger<OpenAiLlmProvider> logger, int httpTimeoutSeconds)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        // Bound every outbound call so a hung provider cannot stall the run.
        _httpClient.Timeout = TimeSpan.FromSeconds(httpTimeoutSeconds > 0 ? httpTimeoutSeconds : 60);
    }

    public async Task<LlmResponse> GenerateCompletionAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        var resolvedModel = ResolveModelName(prompt.ModelName);
        var requestUrl = $"{_config.BaseUrl.TrimEnd('/')}/chat/completions";

        var payload = new
        {
            model = resolvedModel,
            messages = prompt.Messages.Select(m => new { role = m.Role, content = m.Content }),
            temperature = prompt.Temperature,
            max_tokens = prompt.MaxTokens
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        _logger.LogInformation("Sending completion request to OpenAI ({Model})", resolvedModel);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        int promptTokens = 0, completionTokens = 0, totalTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
            completionTokens = usage.TryGetProperty("completion_tokens", out var ctProp) ? ctProp.GetInt32() : 0;
            totalTokens = usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : (promptTokens + completionTokens);
        }

        return new LlmResponse(content, promptTokens, completionTokens, totalTokens, resolvedModel);
    }

    private string ResolveModelName(string requestedModel)
    {
        if (requestedModel.Contains("expensive", StringComparison.OrdinalIgnoreCase))
            return _config.ExpensiveModel;
        if (requestedModel.Contains("cheap", StringComparison.OrdinalIgnoreCase))
            return _config.CheapModel;
        return string.IsNullOrWhiteSpace(requestedModel) ? _config.CheapModel : requestedModel;
    }
}
