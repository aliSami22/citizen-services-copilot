using System.Text;
using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Infrastructure.Llm;

public class OllamaLlmProvider : ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly OllamaConfig _config;
    private readonly ILogger<OllamaLlmProvider> _logger;

    public string ProviderName => "Ollama";

    public OllamaLlmProvider(HttpClient httpClient, OllamaConfig config, ILogger<OllamaLlmProvider> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<LlmResponse> GenerateCompletionAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        var resolvedModel = ResolveModelName(prompt.ModelName);
        var requestUrl = $"{_config.BaseUrl.TrimEnd('/')}/api/chat";

        var payload = new
        {
            model = resolvedModel,
            messages = prompt.Messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false,
            options = new
            {
                temperature = prompt.Temperature,
                num_predict = prompt.MaxTokens
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _logger.LogInformation("Sending chat request to Ollama ({Model})", resolvedModel);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var content = root.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        int promptTokens = root.TryGetProperty("prompt_eval_count", out var pe) ? pe.GetInt32() : (prompt.Messages.Sum(m => m.Content.Length) / 4);
        int completionTokens = root.TryGetProperty("eval_count", out var ec) ? ec.GetInt32() : (content.Length / 4);
        int totalTokens = promptTokens + completionTokens;

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
