using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces.Ingestion;

/// <summary>
/// Contract for coordinating document ingestion, deduplication, chunking, and persistence.
/// </summary>
public interface IDocumentIngestionService
{
    /// <summary>
    /// Ingests a raw text document with deterministic SHA-256 deduplication and chunk persistence.
    /// </summary>
    Task<IngestionResult> IngestTextAsync(IngestTextCommand command, CancellationToken ct = default);
}
