using System.Security.Cryptography;
using System.Text;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Interfaces.Ingestion;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Enums;

namespace CitizenServicesCopilot.Application.Services.Ingestion;

/// <summary>
/// Orchestrates idempotent document ingestion, extraction, chunking, vector embedding, and persistence.
/// </summary>
public class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IEnumerable<IDocumentExtractor> _extractors;
    private readonly IDocumentChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;

    public DocumentIngestionService(
        IDocumentRepository documentRepository,
        IEnumerable<IDocumentExtractor> extractors,
        IDocumentChunker chunker,
        IEmbeddingGenerator embeddingGenerator)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _extractors = extractors ?? throw new ArgumentNullException(nameof(extractors));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
    }

    public async Task<IngestionResult> IngestTextAsync(IngestTextCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            throw new ArgumentException("Document title cannot be empty.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.Source))
        {
            throw new ArgumentException("Document source cannot be empty.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.Content))
        {
            throw new ArgumentException("Document content cannot be empty.", nameof(command));
        }

        // 1. Compute deterministic SHA-256 hash of UTF-8 content
        var contentBytes = Encoding.UTF8.GetBytes(command.Content);
        var hashBytes = SHA256.HashData(contentBytes);
        var contentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // 2. Pre-ingestion idempotency check (duplicate bypass: zero chunking or embedding calls)
        var existingDoc = await _documentRepository.GetByContentHashAsync(contentHash, ct);
        if (existingDoc != null)
        {
            return new IngestionResult(
                DocumentId: existingDoc.Id,
                Status: existingDoc.Status,
                ChunkCount: existingDoc.Chunks?.Count ?? 0,
                ContentHash: contentHash,
                IsDuplicate: true
            );
        }

        var documentId = Guid.NewGuid();

        // 3. Resolve extractor and extract content
        var input = new DocumentSourceInput(
            Title: command.Title,
            Source: command.Source,
            Version: string.IsNullOrWhiteSpace(command.Version) ? "1.0" : command.Version,
            Category: string.IsNullOrWhiteSpace(command.Category) ? "General" : command.Category,
            RawText: command.Content
        );

        var extractor = _extractors.FirstOrDefault(e => e.CanExtract(input));
        if (extractor == null)
        {
            return new IngestionResult(
                DocumentId: documentId,
                Status: IngestionStatus.Failed,
                ChunkCount: 0,
                ContentHash: contentHash,
                IsDuplicate: false,
                FailureReason: "No suitable document extractor found for the provided input."
            );
        }

        ExtractedDocument extractedDoc;
        try
        {
            extractedDoc = await extractor.ExtractAsync(input, ct);
        }
        catch
        {
            return new IngestionResult(
                DocumentId: documentId,
                Status: IngestionStatus.Failed,
                ChunkCount: 0,
                ContentHash: contentHash,
                IsDuplicate: false,
                FailureReason: "Document extraction failed during content processing."
            );
        }

        // 4. Segment content into chunks
        IReadOnlyList<DocumentChunk> chunks;
        try
        {
            chunks = _chunker.Chunk(documentId, extractedDoc);
        }
        catch
        {
            return new IngestionResult(
                DocumentId: documentId,
                Status: IngestionStatus.Failed,
                ChunkCount: 0,
                ContentHash: contentHash,
                IsDuplicate: false,
                FailureReason: "Document chunking failed during content segmentation."
            );
        }

        // 5. Generate vector embeddings for chunks
        if (chunks.Count > 0)
        {
            try
            {
                var chunkTexts = chunks.Select(c => c.Content).ToList();
                var embeddings = await _embeddingGenerator.GenerateEmbeddingsBatchAsync(chunkTexts, ct);

                for (int i = 0; i < chunks.Count && i < embeddings.Count; i++)
                {
                    chunks[i].Embedding = embeddings[i];
                }
            }
            catch
            {
                return new IngestionResult(
                    DocumentId: documentId,
                    Status: IngestionStatus.Failed,
                    ChunkCount: 0,
                    ContentHash: contentHash,
                    IsDuplicate: false,
                    FailureReason: "Vector embedding generation failed during document ingestion."
                );
            }
        }

        // 6. Build domain entity and persist
        var document = new Document
        {
            Id = documentId,
            Title = command.Title,
            Source = command.Source,
            Version = string.IsNullOrWhiteSpace(command.Version) ? "1.0" : command.Version,
            Category = string.IsNullOrWhiteSpace(command.Category) ? "General" : command.Category,
            Content = command.Content,
            ContentHash = contentHash,
            Status = IngestionStatus.Completed,
            CreatedAtUtc = DateTime.UtcNow,
            Chunks = chunks.ToList()
        };

        try
        {
            await _documentRepository.AddAsync(document, ct);

            return new IngestionResult(
                DocumentId: documentId,
                Status: IngestionStatus.Completed,
                ChunkCount: chunks.Count,
                ContentHash: contentHash,
                IsDuplicate: false
            );
        }
        catch
        {
            // Safeguard against concurrent race condition on duplicate ContentHash
            var raceDoc = await _documentRepository.GetByContentHashAsync(contentHash, ct);
            if (raceDoc != null)
            {
                return new IngestionResult(
                    DocumentId: raceDoc.Id,
                    Status: raceDoc.Status,
                    ChunkCount: raceDoc.Chunks?.Count ?? 0,
                    ContentHash: contentHash,
                    IsDuplicate: true
                );
            }

            return new IngestionResult(
                DocumentId: documentId,
                Status: IngestionStatus.Failed,
                ChunkCount: 0,
                ContentHash: contentHash,
                IsDuplicate: false,
                FailureReason: "An error occurred while saving the document to the repository."
            );
        }
    }
}
