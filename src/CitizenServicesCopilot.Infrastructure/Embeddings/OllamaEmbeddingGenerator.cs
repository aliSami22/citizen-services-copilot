using System.Text;
using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Infrastructure.Embeddings;

/// <summary>
/// Ollama local implementation of IEmbeddingGenerator via direct REST HTTP requests.
/// Targets /api/embed endpoint.
/// </summary>
public class OllamaEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly HttpClient _httpClient;
    private readonly OllamaConfig _config;
    private readonly ILogger<OllamaEmbeddingGenerator> _logger;

    public int Dimensions => 768;

    public OllamaEmbeddingGenerator(HttpClient httpClient, OllamaConfig config, ILogger<OllamaEmbeddingGenerator> logger)
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

        var requestUrl = $"{_config.BaseUrl.TrimEnd('/')}/api/embed";
        var model = string.IsNullOrWhiteSpace(_config.EmbeddingModel) ? "nomic-embed-text" : _config.EmbeddingModel;

        var payload = new
        {
            model = model,
            input = texts
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _logger.LogInformation("Requesting embeddings for {Count} items from Ollama ({Model}).", texts.Count, model);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var results = new List<float[]>();

        if (doc.RootElement.TryGetProperty("embeddings", out var embeddingsArray))
        {
            foreach (var vectorElement in embeddingsArray.EnumerateArray())
            {
                var vector = new float[vectorElement.GetArrayLength()];
                int i = 0;
                foreach (var val in vectorElement.EnumerateArray())
                {
                    vector[i++] = val.GetSingle();
                }

                results.Add(vector);
            }
        }

        return results;
    }
}
