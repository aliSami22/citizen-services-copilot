namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Provider-agnostic contract for generating vector embeddings from text inputs.
/// Decouples Domain and Application layers from external AI SDKs and embedding providers.
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>
    /// Vector embedding dimensionality (e.g., 1536).
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Generates a normalized vector embedding for a single text input.
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Generates normalized vector embeddings for a batch of text inputs.
    /// </summary>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
