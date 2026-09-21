using System.Net;
using System.Text;
using System.Text.Json;
using CitizenServicesCopilot.Infrastructure.Embeddings;
using CitizenServicesCopilot.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;

namespace CitizenServicesCopilot.UnitTests.Embeddings;

/// <summary>
/// Guards the 768-dimension contract of the OpenAI-compatible embedding
/// generator: Gemini's native output must pass through untouched (no padding,
/// no truncation), and the request must batch through the `input` array.
/// </summary>
public class OpenAiEmbeddingGeneratorTests
{
    private const int NativeDimensions = 768;

    [Fact]
    public void Dimensions_AreNative768()
    {
        var generator = CreateGenerator(new StubHttpMessageHandler(BuildEmbeddingResponse(NativeDimensions)));

        Assert.Equal(NativeDimensions, generator.Dimensions);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_Native768Vector_IsReturnedUnchanged()
    {
        var handler = new StubHttpMessageHandler(BuildEmbeddingResponse(NativeDimensions));
        var generator = CreateGenerator(handler);

        var vector = await generator.GenerateEmbeddingAsync("ما هي الأوراق المطلوبة لتجديد بطاقة الرقم القومي؟");

        Assert.Equal(NativeDimensions, vector.Length);
        Assert.Equal(1f, vector[0]);
        Assert.Equal(NativeDimensions, vector[NativeDimensions - 1]);
    }

    [Fact]
    public async Task GenerateEmbeddingsBatchAsync_SendsInputAsArray()
    {
        var handler = new StubHttpMessageHandler(BuildBatchEmbeddingResponse(NativeDimensions, 2));
        var generator = CreateGenerator(handler);

        var vectors = await generator.GenerateEmbeddingsBatchAsync(new[] { "one", "two" });

        Assert.Equal(2, vectors.Count);
        Assert.All(vectors, v => Assert.Equal(NativeDimensions, v.Length));

        using var request = JsonDocument.Parse(handler.LastRequestBody!);
        var input = request.RootElement.GetProperty("input");
        Assert.Equal(JsonValueKind.Array, input.ValueKind);
        Assert.Equal(2, input.GetArrayLength());
    }

    private static OpenAiEmbeddingGenerator CreateGenerator(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            new OpenAiConfig { ApiKey = "test-key-not-a-real-secret", EmbeddingModel = "text-embedding-004" },
            NullLogger<OpenAiEmbeddingGenerator>.Instance);

    private static string BuildEmbeddingResponse(int dimensions)
        => BuildBatchEmbeddingResponse(dimensions, 1);

    private static string BuildBatchEmbeddingResponse(int dimensions, int items)
    {
        var data = Enumerable.Range(0, items)
            .Select(index => new
            {
                index,
                embedding = Enumerable.Range(1, dimensions).Select(i => (double)i).ToArray()
            })
            .ToArray();

        return JsonSerializer.Serialize(new { data });
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(string responseJson) => _responseJson = responseJson;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
