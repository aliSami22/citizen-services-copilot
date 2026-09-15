using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces.Ingestion;

/// <summary>
/// Contract for extracting raw content and metadata from document sources.
/// </summary>
public interface IDocumentExtractor
{
    /// <summary>
    /// Checks whether the extractor supports the provided document source input.
    /// </summary>
    bool CanExtract(DocumentSourceInput input);

    /// <summary>
    /// Extracts structured content and metadata from the document source input.
    /// </summary>
    Task<ExtractedDocument> ExtractAsync(DocumentSourceInput input, CancellationToken ct = default);
}
