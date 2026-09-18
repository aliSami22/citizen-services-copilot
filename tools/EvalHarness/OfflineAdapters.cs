using System.Text;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CitizenServicesCopilot.EvalHarness;

/// <summary>
/// In-memory AppDbContext for offline evaluation. Bypasses pgvector/postgres
/// configuration that is incompatible with the InMemory provider, mirroring the
/// pattern used in GroundedRetrieverTests.TestAppDbContext.
/// </summary>
public sealed class OfflineEvalDbContext : AppDbContext
{
    public OfflineEvalDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Value object reachable via InquiryDraft.Citations (JSON column in the real
        // model); InMemory validation rejects it as a keyless entity.
        modelBuilder.Ignore<CitizenServicesCopilot.Domain.ValueObjects.Citation>();

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
            b.Property(c => c.Embedding);
        });
    }
}

/// <summary>
/// Deterministic, cross-process-stable IEmbeddingGenerator.
/// Seeds from SHA-256 so identical inputs yield identical vectors in any process;
/// deliberately avoids string.GetHashCode(), whose seed is randomized per-process
/// in .NET Core and would make evaluation results non-reproducible.
/// </summary>
public sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator
{
    public int Dimensions { get; }

    public DeterministicEmbeddingGenerator(int dimensions = 64)
    {
        Dimensions = dimensions;
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        => Task.FromResult(CreateDeterministicVector(text));

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(CreateDeterministicVector).ToList());

    private float[] CreateDeterministicVector(string text)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var random = new Random(BitConverter.ToInt32(hash, 0));

        var vector = new float[Dimensions];
        for (int i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        float sumSquares = 0f;
        for (int i = 0; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        float norm = (float)Math.Sqrt(sumSquares);
        if (norm > 0f)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}