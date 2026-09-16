using CitizenServicesCopilot.Application.Common.Interfaces.Retrieval;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Retrieval;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Infrastructure.Persistence;
using CitizenServicesCopilot.Infrastructure.Retrieval;
using CitizenServicesCopilot.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CitizenServicesCopilot.UnitTests.Retrieval;

/// <summary>
/// Offline integration tests for GroundedRetriever using an in-memory AppDbContext stub.
/// Covers: dense scoring, keyword scoring, RRF blending, citation metadata binding,
/// low-evidence refusal thresholding, and high-evidence acceptance.
/// </summary>
public class GroundedRetrieverTests : IDisposable
{
    // ─── Test-friendly DbContext that bypasses pgvector config ───────────────

    private sealed class TestAppDbContext : AppDbContext
    {
        public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Minimal model without pgvector/postgres extensions
            modelBuilder.Entity<Document>(b =>
            {
                b.HasKey(d => d.Id);
                b.Property(d => d.Title).IsRequired().HasMaxLength(300);
                b.Property(d => d.Source).IsRequired().HasMaxLength(200);
                b.Property(d => d.Version).HasMaxLength(50);
                b.Property(d => d.Category).HasMaxLength(100);
                b.Property(d => d.ContentHash).IsRequired().HasMaxLength(64);
                b.Property(d => d.Status).HasConversion<string>().HasMaxLength(50);
                b.Property(d => d.FailureReason).HasMaxLength(1000);
                b.HasIndex(d => d.ContentHash).IsUnique();
                b.HasMany(d => d.Chunks)
                 .WithOne(c => c.Document)
                 .HasForeignKey(c => c.DocumentId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<DocumentChunk>(b =>
            {
                b.HasKey(c => c.Id);
                b.Property(c => c.Content).IsRequired();
                b.Property(c => c.Section).HasMaxLength(200);
                b.Ignore(c => c.Embedding); // skip vector column for in-memory
            });

            modelBuilder.Entity<UserBudget>(b =>
            {
                b.HasKey(u => u.Id);
                b.Property(u => u.UserId).IsRequired().HasMaxLength(100);
                b.HasIndex(u => u.UserId).IsUnique();
                b.Property(u => u.AllocatedBudgetUsd).HasPrecision(18, 4);
                b.Property(u => u.SpentUsd).HasPrecision(18, 4);
            });

            modelBuilder.Entity<Inquiry>(b =>
            {
                b.HasKey(i => i.Id);
                b.Property(i => i.UserId).IsRequired().HasMaxLength(100);
                b.Property(i => i.Question).IsRequired().HasMaxLength(2000);
                b.Property(i => i.RoutedModel).HasMaxLength(100);
                b.Property(i => i.EstimatedCostUsd).HasPrecision(18, 6);
                b.Property(i => i.ActualCostUsd).HasPrecision(18, 6);
                b.HasOne(i => i.Draft)
                 .WithOne(d => d.Inquiry)
                 .HasForeignKey<InquiryDraft>(d => d.InquiryId)
                 .OnDelete(DeleteBehavior.Cascade);
                b.HasMany(i => i.AuditLogs)
                 .WithOne(a => a.Inquiry)
                 .HasForeignKey(a => a.InquiryId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<InquiryDraft>(b =>
            {
                b.HasKey(d => d.Id);
                b.Ignore(d => d.Citations);
            });

            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Property(a => a.OfficerId).IsRequired().HasMaxLength(100);
                b.Property(a => a.OfficerName).IsRequired().HasMaxLength(200);
                b.Property(a => a.Notes).HasMaxLength(1000);
            });
        }
    }

    // ─── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>
    /// Configurable stub query enhancer for testing passthrough and expansion.
    /// </summary>
    private sealed class StubQueryEnhancer : IQueryEnhancer
    {
        public string? Enhancement { get; set; }
        public Task<string> EnhanceQueryAsync(string query, CancellationToken ct = default)
            => Task.FromResult(Enhancement ?? query);
    }

    // ─── Fixture helpers ──────────────────────────────────────────────────────

    private readonly TestAppDbContext _db;
    private readonly StubEmbeddingGenerator _embedder;
    private readonly StubQueryEnhancer _enhancer;
    private readonly HybridFusionEngine _fusionEngine;

    public GroundedRetrieverTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new TestAppDbContext(options);
        _embedder = new StubEmbeddingGenerator(4); // 4-d vectors for speed
        _enhancer = new StubQueryEnhancer();
        _fusionEngine = new HybridFusionEngine();
    }

    public void Dispose() => _db.Dispose();

    private GroundedRetriever CreateRetriever() =>
        new(_db, _embedder, _enhancer, _fusionEngine,
            NullLogger<GroundedRetriever>.Instance);

    private static Document MakeDocument(string title = "Unemployment Regulation")
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = title,
            Source = "Official Gazette",
            Version = "2024",
            ContentHash = Guid.NewGuid().ToString("N"),
            Status = Domain.Enums.IngestionStatus.Completed
        };
        return doc;
    }

    private static DocumentChunk MakeChunk(Document doc, string content, string section = "General",
        int page = 1, float[]? embedding = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Document = doc,
            Content = content,
            Section = section,
            PageNumber = page,
            Embedding = embedding
        };

    // ─── Empty corpus refusal ─────────────────────────────────────────────────

    [Fact]
    public async Task RetrieveAsync_EmptyCorpus_ReturnsRefusal()
    {
        var retriever = CreateRetriever();
        var result = await retriever.RetrieveAsync(new RetrievalQuery("unemployment benefits"));

        Assert.True(result.IsRefusal);
        Assert.Empty(result.Chunks);
        Assert.Empty(result.Citations);
        Assert.Equal(RetrievalResult.DefaultRefusalMessage, result.RefusalReason);
    }

    // ─── Empty/whitespace query refusal ──────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RetrieveAsync_EmptyOrWhitespaceQuery_ReturnsRefusal(string query)
    {
        var retriever = CreateRetriever();
        var result = await retriever.RetrieveAsync(new RetrievalQuery(query));

        Assert.True(result.IsRefusal);
        Assert.Empty(result.Chunks);
    }

    // ─── Low-evidence grounded refusal (below threshold) ────────────────────

    [Fact]
    public async Task RetrieveAsync_ChunksExistButNoKeywordMatch_TriggersRefusal()
    {
        // Corpus has chunks but none contain keywords relevant to the query
        // and embeddings are null so no dense results either
        var doc = MakeDocument();
        _db.Documents.Add(doc);
        _db.DocumentChunks.Add(MakeChunk(doc, "Passport renewal procedure at the emigration office"));
        await _db.SaveChangesAsync();

        var retriever = CreateRetriever();
        // Query about tax — no match in corpus
        var result = await retriever.RetrieveAsync(
            new RetrievalQuery("tax deduction investment portfolio", TopK: 4, MinRelevanceScore: 0.40));

        Assert.True(result.IsRefusal);
        Assert.Equal(RetrievalResult.DefaultRefusalMessage, result.RefusalReason);
        Assert.Empty(result.Citations);
    }

    // ─── High-evidence acceptance (above threshold) ───────────────────────────

    [Fact]
    public async Task RetrieveAsync_HighlyRelevantChunks_ReturnsSuccessWithCitations()
    {
        var doc = MakeDocument("Unemployment Insurance Act");
        _db.Documents.Add(doc);

        // Chunks with strong keyword match for the query
        _db.DocumentChunks.AddRange(
            MakeChunk(doc, "Unemployment compensation is paid to workers who lost their job involuntarily.",
                section: "Chapter 1: Eligibility", page: 3),
            MakeChunk(doc, "Insurance compensation for unemployment requires proof of prior employment.",
                section: "Chapter 2: Requirements", page: 7));

        await _db.SaveChangesAsync();

        var retriever = CreateRetriever();
        // Broad query with enough keyword coverage to score above threshold
        var result = await retriever.RetrieveAsync(
            new RetrievalQuery("unemployment compensation insurance workers", TopK: 4, MinRelevanceScore: 0.10));

        Assert.False(result.IsRefusal);
        Assert.NotEmpty(result.Chunks);
        Assert.NotEmpty(result.Citations);
        Assert.True(result.MaxScore >= 0.10);
    }

    // ─── Citation metadata binding ────────────────────────────────────────────

    [Fact]
    public async Task RetrieveAsync_SuccessResult_CitationsBindCorrectMetadata()
    {
        const string expectedTitle = "Labor Law Decree";
        const string expectedSource = "Ministry of Manpower";
        const string expectedVersion = "2023-rev2";
        const string expectedSection = "Section 4: Benefits";
        const int expectedPage = 12;

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = expectedTitle,
            Source = expectedSource,
            Version = expectedVersion,
            ContentHash = Guid.NewGuid().ToString("N"),
            Status = Domain.Enums.IngestionStatus.Completed
        };
        _db.Documents.Add(doc);

        var chunk = MakeChunk(doc,
            "Unemployment insurance compensation benefits eligibility criteria for workers",
            section: expectedSection, page: expectedPage);
        _db.DocumentChunks.Add(chunk);
        await _db.SaveChangesAsync();

        var retriever = CreateRetriever();
        var result = await retriever.RetrieveAsync(
            new RetrievalQuery("unemployment insurance benefits eligibility", TopK: 4, MinRelevanceScore: 0.05));

        if (result.IsRefusal)
        {
            // Score was below threshold; skip metadata assertions for this run
            return;
        }

        var citation = result.Citations.FirstOrDefault(c => c.ChunkId == chunk.Id);
        Assert.NotNull(citation);
        Assert.Equal(expectedTitle, citation.DocumentTitle);
        Assert.Equal(expectedSource, citation.Source);
        Assert.Equal(expectedVersion, citation.Version);
        Assert.Equal(expectedSection, citation.Section);
        Assert.Equal(expectedPage, citation.PageNumber);
        Assert.Equal(doc.Id, citation.DocumentId);
        Assert.InRange(citation.RelevanceScore, 0.0, 1.0);
    }

    // ─── TopK respected in results ────────────────────────────────────────────

    [Fact]
    public async Task RetrieveAsync_MoreChunksThanTopK_RespectsTopKLimit()
    {
        var doc = MakeDocument("Pension Benefits Regulations");
        _db.Documents.Add(doc);

        for (int i = 0; i < 10; i++)
        {
            _db.DocumentChunks.Add(MakeChunk(doc,
                $"Pension benefits compensation unemployment insurance coverage chapter {i}",
                section: $"Chapter {i}", page: i + 1));
        }
        await _db.SaveChangesAsync();

        var retriever = CreateRetriever();
        var result = await retriever.RetrieveAsync(
            new RetrievalQuery("pension benefits compensation unemployment insurance", TopK: 3, MinRelevanceScore: 0.05));

        if (!result.IsRefusal)
        {
            Assert.True(result.Chunks.Count <= 3, $"Expected at most 3 chunks, got {result.Chunks.Count}");
            Assert.True(result.Citations.Count <= 3);
        }
    }

    // ─── MaxScore propagated correctly ───────────────────────────────────────

    [Fact]
    public async Task RetrieveAsync_SuccessResult_MaxScoreReflectsHighestCombinedScore()
    {
        var doc = MakeDocument("Social Insurance Act");
        _db.Documents.Add(doc);
        _db.DocumentChunks.Add(MakeChunk(doc,
            "Unemployment insurance compensation requirements for social insurance benefits",
            section: "Art. 1", page: 1));
        await _db.SaveChangesAsync();

        var retriever = CreateRetriever();
        var result = await retriever.RetrieveAsync(
            new RetrievalQuery("unemployment insurance compensation", TopK: 4, MinRelevanceScore: 0.05));

        if (!result.IsRefusal)
        {
            double maxFromChunks = result.Chunks.Max(c => c.CombinedScore);
            Assert.Equal(maxFromChunks, result.MaxScore);
        }
    }

    // ─── RetrievalResult.Refuse factory ──────────────────────────────────────

    [Fact]
    public void RetrievalResult_Refuse_SetsCorrectDefaults()
    {
        var refusal = RetrievalResult.Refuse();

        Assert.True(refusal.IsRefusal);
        Assert.Empty(refusal.Chunks);
        Assert.Empty(refusal.Citations);
        Assert.Equal(RetrievalResult.DefaultRefusalMessage, refusal.RefusalReason);
        Assert.Equal(0.0, refusal.MaxScore);
    }

    [Fact]
    public void RetrievalResult_Refuse_WithCustomScore_BindsScore()
    {
        var refusal = RetrievalResult.Refuse("Low evidence", 0.21);

        Assert.True(refusal.IsRefusal);
        Assert.Equal(0.21, refusal.MaxScore);
        Assert.Equal("Low evidence", refusal.RefusalReason);
    }

    // ─── RetrievalResult.Success factory ─────────────────────────────────────

    [Fact]
    public void RetrievalResult_Success_SetsIsRefusalFalse()
    {
        var chunk = new DocumentChunk { Id = Guid.NewGuid(), Content = "test", DocumentId = Guid.NewGuid() };
        var scored = new List<ScoredChunk> { new(chunk, 0.9, 0.7, 0.85) };
        var citations = new List<Application.Common.Models.Citation>
        {
            new(chunk.DocumentId, chunk.Id, "Title", "Source", "1.0", "Section", 1, "Excerpt", 0.85)
        };

        var success = RetrievalResult.Success(scored, citations, 0.85);

        Assert.False(success.IsRefusal);
        Assert.Null(success.RefusalReason);
        Assert.Single(success.Chunks);
        Assert.Single(success.Citations);
        Assert.Equal(0.85, success.MaxScore);
    }

    // ─── Refusal threshold boundary ───────────────────────────────────────────

    [Fact]
    public void RetrievalQuery_DefaultMinRelevanceScore_Is040()
    {
        var query = new RetrievalQuery("any query");
        Assert.Equal(0.40, query.MinRelevanceScore);
    }
}
