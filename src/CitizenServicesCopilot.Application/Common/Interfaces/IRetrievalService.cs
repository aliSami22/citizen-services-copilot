using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

public interface IRetrievalService
{
    Task<IReadOnlyList<DocumentChunk>> RetrieveRelevantChunksAsync(
        string query, 
        int topK = 4, 
        double minSimilarity = 0.45, 
        CancellationToken ct = default);
}
