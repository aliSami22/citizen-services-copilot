using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Provider-independent contract for hybrid document retrieval and evidence grounding.
/// </summary>
public interface IRetrievalService
{
    /// <summary>
    /// Executes hybrid retrieval (dense + keyword fusion) and returns scored chunks or grounded refusal.
    /// </summary>
    Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default);

    /// <summary>
    /// Backwards-compatible overload returning raw DocumentChunks for simple consumers.
    /// </summary>
    async Task<IReadOnlyList<DocumentChunk>> RetrieveRelevantChunksAsync(
        string query,
        int topK = 4,
        double minSimilarity = 0.40,
        CancellationToken ct = default)
    {
        var result = await RetrieveAsync(new RetrievalQuery(query, topK, minSimilarity), ct);
        return result.Chunks.Select(c => c.Chunk).ToList();
    }
}
