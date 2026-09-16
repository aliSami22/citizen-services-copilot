using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Services.Retrieval;

/// <summary>
/// Pure algorithmic engine implementing Reciprocal Rank Fusion (RRF) to blend dense and keyword search rankings.
/// </summary>
public class HybridFusionEngine
{
    private const int DefaultK = 60;

    /// <summary>
    /// Fuses ranked candidate lists from dense vector search and keyword lexical search into a unified scored list.
    /// </summary>
    /// <param name="denseRanked">Chunks ordered descending by vector cosine similarity.</param>
    /// <param name="keywordRanked">Chunks ordered descending by keyword relevance score.</param>
    /// <param name="topK">Maximum number of fused candidates to return.</param>
    /// <param name="k">RRF smoothing constant (default 60).</param>
    /// <returns>Ranked list of ScoredChunk entities sorted descending by normalized combined score.</returns>
    public IReadOnlyList<ScoredChunk> Fuse(
        IReadOnlyList<(DocumentChunk Chunk, double Score)> denseRanked,
        IReadOnlyList<(DocumentChunk Chunk, double Score)> keywordRanked,
        int topK = 4,
        int k = DefaultK)
    {
        ArgumentNullException.ThrowIfNull(denseRanked);
        ArgumentNullException.ThrowIfNull(keywordRanked);

        if (denseRanked.Count == 0 && keywordRanked.Count == 0)
        {
            return Array.Empty<ScoredChunk>();
        }

        // Map chunk IDs to their best scores and 1-based ranks
        var chunkMap = new Dictionary<Guid, (DocumentChunk Chunk, double DenseScore, double KeywordScore, int? DenseRank, int? KeywordRank)>();

        for (int i = 0; i < denseRanked.Count; i++)
        {
            var item = denseRanked[i];
            chunkMap[item.Chunk.Id] = (item.Chunk, item.Score, 0.0, i + 1, null);
        }

        for (int i = 0; i < keywordRanked.Count; i++)
        {
            var item = keywordRanked[i];
            if (chunkMap.TryGetValue(item.Chunk.Id, out var existing))
            {
                chunkMap[item.Chunk.Id] = (existing.Chunk, existing.DenseScore, item.Score, existing.DenseRank, i + 1);
            }
            else
            {
                chunkMap[item.Chunk.Id] = (item.Chunk, 0.0, item.Score, null, i + 1);
            }
        }

        // Theoretical maximum RRF score for normalization (rank 1 in both lists)
        double maxTheoreticalRrf = (1.0 / (k + 1)) + (1.0 / (k + 1));

        var fusedResults = new List<ScoredChunk>();

        foreach (var entry in chunkMap.Values)
        {
            double rrfDense = entry.DenseRank.HasValue ? 1.0 / (k + entry.DenseRank.Value) : 0.0;
            double rrfKeyword = entry.KeywordRank.HasValue ? 1.0 / (k + entry.KeywordRank.Value) : 0.0;

            double rawRrf = rrfDense + rrfKeyword;
            double normalizedScore = Math.Min(1.0, rawRrf / maxTheoreticalRrf);

            fusedResults.Add(new ScoredChunk(
                Chunk: entry.Chunk,
                DenseScore: entry.DenseScore,
                KeywordScore: entry.KeywordScore,
                CombinedScore: Math.Round(normalizedScore, 4)
            ));
        }

        return fusedResults
            .OrderByDescending(s => s.CombinedScore)
            .ThenByDescending(s => s.DenseScore)
            .Take(topK)
            .ToList();
    }
}
