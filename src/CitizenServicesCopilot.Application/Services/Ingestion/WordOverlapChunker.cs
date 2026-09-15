using CitizenServicesCopilot.Application.Common.Interfaces.Ingestion;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Services.Ingestion;

/// <summary>
/// Baseline chunking strategy that segments document text into fixed word-count windows with sliding overlap.
/// Note: This is a documented baseline (500 words / 50-word overlap) for FR-1, not an optimized semantic chunker.
/// </summary>
public class WordOverlapChunker : IDocumentChunker
{
    private readonly int _chunkSizeInWords;
    private readonly int _overlapWords;

    public WordOverlapChunker(int chunkSizeInWords = 500, int overlapWords = 50)
    {
        if (chunkSizeInWords <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSizeInWords), "Chunk size must be greater than zero.");
        if (overlapWords < 0 || overlapWords >= chunkSizeInWords)
            throw new ArgumentOutOfRangeException(nameof(overlapWords), "Overlap must be non-negative and strictly less than chunk size.");

        _chunkSizeInWords = chunkSizeInWords;
        _overlapWords = overlapWords;
    }

    public int ChunkSizeInWords => _chunkSizeInWords;
    public int OverlapWords => _overlapWords;

    /// <summary>
    /// Chunks extracted document content into DocumentChunk entities preserving metadata.
    /// Raw-text convention: PageNumber is set to 1 because raw text lacks pagination metadata.
    /// </summary>
    public IReadOnlyList<DocumentChunk> Chunk(Guid documentId, ExtractedDocument extractedDocument)
    {
        ArgumentNullException.ThrowIfNull(extractedDocument);

        if (string.IsNullOrWhiteSpace(extractedDocument.Content))
        {
            return Array.Empty<DocumentChunk>();
        }

        var words = extractedDocument.Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return Array.Empty<DocumentChunk>();
        }

        var chunks = new List<DocumentChunk>();
        int startIndex = 0;
        int chunkIndex = 0;
        int step = Math.Max(1, _chunkSizeInWords - _overlapWords);

        string section = string.IsNullOrWhiteSpace(extractedDocument.Category)
            ? "General"
            : extractedDocument.Category;

        while (startIndex < words.Length)
        {
            int count = Math.Min(_chunkSizeInWords, words.Length - startIndex);
            var chunkText = string.Join(" ", words, startIndex, count);

            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Content = chunkText,
                PageNumber = 1, // Documented raw-text convention: defaults to 1 for unpaginated raw text
                Section = section,
                ChunkIndex = chunkIndex++,
                Embedding = null // Vectorization/embeddings explicitly out of scope for this baseline
            });

            if (startIndex + count >= words.Length)
            {
                break;
            }

            startIndex += step;
        }

        return chunks;
    }
}
