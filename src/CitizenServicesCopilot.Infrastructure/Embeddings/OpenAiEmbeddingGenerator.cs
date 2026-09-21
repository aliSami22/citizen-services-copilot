using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Infrastructure.Embeddings;

/// <summary>
/// OpenAI-compatible implementation of IEmbeddingGenerator via direct REST HTTP
/// requests. Works with any OpenAI-compatible provider (OpenAI, Gemini
/// compatibility endpoint, ...). The configured model determines the native
/// output dimensionality; the current corpus targets 768 dimensions.
/// </summary>
public class OpenAiEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiConfig _config;
    private readonly ILogger<OpenAiEmbeddingGenerator> _logger;

    public int Dimensions => 768;

    public OpenAiEmbeddingGenerator(HttpClient httpClient, OpenAiConfig config, ILogger<OpenAiEmbeddingGenerator> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[Dimensions];
        }

        var results = await GenerateEmbeddingsBatchAsync(new[] { text }, ct);
        return results.FirstOrDefault() ?? new float[Dimensions];
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var requestUrl = $"{_config.BaseUrl.TrimEnd('/')}/embeddings";
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_config.EmbeddingModel) ? "text-embedding-3-small" : _config.EmbeddingModel,
            input = texts
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

        _logger.LogInformation("Requesting embeddings for {Count} items from OpenAI ({Model}).", texts.Count, payload.model);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var dataArray = doc.RootElement.GetProperty("data");
        var results = new List<(int Index, float[] Vector)>();

        foreach (var item in dataArray.EnumerateArray())
        {
            int index = item.GetProperty("index").GetInt32();
            var vectorElement = item.GetProperty("embedding");
            var vector = new float[vectorElement.GetArrayLength()];
            int i = 0;
            foreach (var val in vectorElement.EnumerateArray())
            {
                vector[i++] = val.GetSingle();
            }
            results.Add((index, vector));
        }

        return results.OrderBy(r => r.Index).Select(r => r.Vector).ToList();
    }
}
