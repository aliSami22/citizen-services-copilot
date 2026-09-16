using System.Security.Cryptography;
using System.Text;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Interfaces.Ingestion;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Ingestion;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Enums;

namespace CitizenServicesCopilot.UnitTests.Ingestion;

public class DocumentIngestionServiceTests
{
    private class InMemoryDocumentRepository : IDocumentRepository
    {
        public List<Document> Documents { get; } = new();
        public int AddCallCount { get; private set; }

        public Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));
        }

        public Task<Document?> GetByContentHashAsync(string contentHash, CancellationToken ct = default)
        {
            return Task.FromResult(Documents.FirstOrDefault(d => d.ContentHash == contentHash));
        }

        public Task AddAsync(Document document, CancellationToken ct = default)
        {
            AddCallCount++;
            Documents.Add(document);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Document document, CancellationToken ct = default)
        {
            var idx = Documents.FindIndex(d => d.Id == document.Id);
            if (idx >= 0) Documents[idx] = document;
            return Task.CompletedTask;
        }
    }

    private class ThrowingExtractor : IDocumentExtractor
    {
        public bool CanExtract(DocumentSourceInput input) => true;

        public Task<ExtractedDocument> ExtractAsync(DocumentSourceInput input, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Extractor corrupted stream");
        }
    }

    private readonly InMemoryDocumentRepository _repo = new();
    private readonly PlainTextExtractor _plainTextExtractor = new();
    private readonly WordOverlapChunker _chunker = new(chunkSizeInWords: 500, overlapWords: 50);

    private DocumentIngestionService CreateService(IEnumerable<IDocumentExtractor>? extractors = null)
    {
        var extractorList = extractors ?? new IDocumentExtractor[] { _plainTextExtractor };
        return new DocumentIngestionService(_repo, extractorList, _chunker);
    }

    [Fact]
    public async Task IngestTextAsync_NewDocument_ExtractsChunksPersistsAndReturnsCompleted()
    {
        var service = CreateService();
        var command = new IngestTextCommand(
            Title: "Citizen Rights Act",
            Source: "ministry-portal/rights",
            Version: "1.2",
            Category: "Civil Rights",
            Content: "All citizens have the right to access governmental services promptly and fairly."
        );

        var result = await service.IngestTextAsync(command);

        Assert.NotEqual(Guid.Empty, result.DocumentId);
        Assert.Equal(IngestionStatus.Completed, result.Status);
        Assert.False(result.IsDuplicate);
        Assert.Equal(1, result.ChunkCount);
        Assert.Null(result.FailureReason);

        // Verify SHA-256 hash
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Content))).ToLowerInvariant();
        Assert.Equal(expectedHash, result.ContentHash);

        // Verify persistence in repository
        Assert.Equal(1, _repo.AddCallCount);
        var savedDoc = _repo.Documents.FirstOrDefault(d => d.Id == result.DocumentId);
        Assert.NotNull(savedDoc);
        Assert.Equal("Citizen Rights Act", savedDoc.Title);
        Assert.Equal("Civil Rights", savedDoc.Category);
        Assert.Equal(expectedHash, savedDoc.ContentHash);
        Assert.Single(savedDoc.Chunks);

        var chunk = savedDoc.Chunks.First();
        Assert.Equal(savedDoc.Id, chunk.DocumentId);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal(1, chunk.PageNumber);
        Assert.Equal("Civil Rights", chunk.Section);
        Assert.Null(chunk.Embedding);
    }

    [Fact]
    public async Task IngestTextAsync_DuplicateContent_ReturnsExistingDocumentWithoutReIngesting()
    {
        var service = CreateService();
        var content = "Identical content for idempotency test.";
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        var existingDoc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Existing Title",
            Source = "source-1",
            Content = content,
            ContentHash = expectedHash,
            Status = IngestionStatus.Completed,
            Chunks = new List<DocumentChunk>
            {
                new() { Id = Guid.NewGuid(), Content = content, ChunkIndex = 0 }
            }
        };
        await _repo.AddAsync(existingDoc);
        var initialAddCount = _repo.AddCallCount;

        var command = new IngestTextCommand(
            Title: "Different Title Same Content",
            Source: "source-2",
            Version: "1.0",
            Category: "General",
            Content: content
        );

        var result = await service.IngestTextAsync(command);

        Assert.Equal(existingDoc.Id, result.DocumentId);
        Assert.True(result.IsDuplicate);
        Assert.Equal(IngestionStatus.Completed, result.Status);
        Assert.Equal(1, result.ChunkCount);
        Assert.Equal(expectedHash, result.ContentHash);
        // Verify no extra AddAsync was called
        Assert.Equal(initialAddCount, _repo.AddCallCount);
    }

    [Theory]
    [InlineData("", "source", "content")]
    [InlineData("   ", "source", "content")]
    [InlineData("title", "", "content")]
    [InlineData("title", "   ", "content")]
    [InlineData("title", "source", "")]
    [InlineData("title", "source", "   ")]
    public async Task IngestTextAsync_InvalidArguments_ThrowsArgumentException(string title, string source, string content)
    {
        var service = CreateService();
        var command = new IngestTextCommand(title, source, "1.0", "General", content);

        await Assert.ThrowsAsync<ArgumentException>(() => service.IngestTextAsync(command));
    }

    [Fact]
    public async Task IngestTextAsync_ExtractorThrows_ReturnsFailedStatusWithReason()
    {
        var service = CreateService(new IDocumentExtractor[] { new ThrowingExtractor() });
        var command = new IngestTextCommand(
            Title: "Failing Document",
            Source: "failing-source",
            Version: "1.0",
            Category: "General",
            Content: "Some content that fails during extraction"
        );

        var result = await service.IngestTextAsync(command);

        Assert.Equal(IngestionStatus.Failed, result.Status);
        Assert.False(result.IsDuplicate);
        Assert.Equal(0, result.ChunkCount);
        Assert.NotNull(result.FailureReason);
        Assert.Equal(0, _repo.AddCallCount);
    }

    [Fact]
    public async Task IngestTextAsync_NoSuitableExtractor_ReturnsFailedStatus()
    {
        var service = CreateService(Array.Empty<IDocumentExtractor>());
        var command = new IngestTextCommand(
            Title: "No Extractor Doc",
            Source: "source",
            Version: "1.0",
            Category: "General",
            Content: "Valid content but no extractors registered"
        );

        var result = await service.IngestTextAsync(command);

        Assert.Equal(IngestionStatus.Failed, result.Status);
        Assert.False(result.IsDuplicate);
        Assert.Contains("No suitable document extractor", result.FailureReason);
    }
}
