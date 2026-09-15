using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Common.Interfaces.Ingestion;

/// <summary>
/// Contract for segmenting extracted document content into DocumentChunk domain entities.
/// </summary>
public interface IDocumentChunker
{
    /// <summary>
    /// Segments an extracted document into DocumentChunk domain entities with complete citation metadata.
    /// </summary>
    /// <param name="documentId">The identifier of the parent Document.</param>
    /// <param name="extractedDocument">The extracted document content and metadata.</param>
    /// <returns>A read-only list of DocumentChunk domain entities ready for storage or vectorization.</returns>
    IReadOnlyList<DocumentChunk> Chunk(Guid documentId, ExtractedDocument extractedDocument);
}
