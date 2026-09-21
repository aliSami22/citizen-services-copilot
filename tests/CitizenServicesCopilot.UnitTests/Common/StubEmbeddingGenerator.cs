using CitizenServicesCopilot.Application.Common.Interfaces;

namespace CitizenServicesCopilot.UnitTests.Common;

/// <summary>
/// Deterministic offline stub for IEmbeddingGenerator used in unit tests.
/// Generates fixed 768-dimensional float arrays without external network/API calls.
/// </summary>
public class StubEmbeddingGenerator : IEmbeddingGenerator
{
    public int Dimensions { get; }
    public int CallCount { get; private set; }
    public int BatchCallCount { get; private set; }
    public int TotalTextsProcessed { get; private set; }
    public bool ShouldThrow { get; set; }

    public StubEmbeddingGenerator(int dimensions = 768)
    {
        Dimensions = dimensions;
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (ShouldThrow)
        {
            throw new HttpRequestException("Simulated embedding API failure");
        }

        CallCount++;
        TotalTextsProcessed++;
        return Task.FromResult(CreateDeterministicVector(text));
    }

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (ShouldThrow)
        {
            throw new HttpRequestException("Simulated embedding API batch failure");
        }

        BatchCallCount++;
        TotalTextsProcessed += texts.Count;

        var results = texts.Select(CreateDeterministicVector).ToList();
        return Task.FromResult<IReadOnlyList<float[]>>(results);
    }

    private float[] CreateDeterministicVector(string text)
    {
        var vector = new float[Dimensions];
        int hash = text.GetHashCode();
        var random = new Random(hash);

        for (int i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        // L2 normalize
        float sumSquares = 0f;
        for (int i = 0; i < Dimensions; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        float norm = (float)Math.Sqrt(sumSquares);
        if (norm > 0f)
        {
            for (int i = 0; i < Dimensions; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}
