using System.Text.Json;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.ValueObjects;
using CitizenServicesCopilot.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace CitizenServicesCopilot.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<UserBudget> UserBudgets => Set<UserBudget>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<InquiryDraft> InquiryDrafts => Set<InquiryDraft>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();
    public DbSet<ApprovalAudit> ApprovalAudits => Set<ApprovalAudit>();
    public DbSet<PersistedDraft> PersistedDrafts => Set<PersistedDraft>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Enable pgvector extension in PostgreSQL
        modelBuilder.HasPostgresExtension("vector");

        // 2. Documents
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

        // 3. DocumentChunks with pgvector column
        var vectorConverter = new ValueConverter<float[]?, Vector?>(
            v => v == null ? null : new Vector(v),
            v => v == null ? null : v.ToArray()
        );

        modelBuilder.Entity<DocumentChunk>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Content).IsRequired();
            b.Property(c => c.Section).HasMaxLength(200);

            b.Property(c => c.Embedding)
             .HasConversion(vectorConverter)
             .HasColumnType("vector(1536)");

            b.HasIndex(c => c.DocumentId);

            b.HasIndex(c => c.Embedding)
             .HasMethod("hnsw")
             .HasOperators("vector_cosine_ops");
        });

        // 4. UserBudgets
        modelBuilder.Entity<UserBudget>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.UserId).IsRequired().HasMaxLength(100);
            b.HasIndex(u => u.UserId).IsUnique();
            b.Property(u => u.AllocatedBudgetUsd).HasPrecision(18, 4);
            b.Property(u => u.SpentUsd).HasPrecision(18, 4);
        });

        // 5. Inquiries & Drafts
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

        // 6. InquiryDrafts with Citations JSON conversion
        var citationsConverter = new ValueConverter<List<Citation>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<Citation>>(v, (JsonSerializerOptions?)null) ?? new List<Citation>()
        );

        modelBuilder.Entity<InquiryDraft>(b =>
        {
            b.HasKey(d => d.Id);
            b.Property(d => d.Citations)
             .HasConversion(citationsConverter)
             .HasColumnType("jsonb");
        });

        // 7. AuditLogs
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.OfficerId).IsRequired().HasMaxLength(100);
            b.Property(a => a.OfficerName).IsRequired().HasMaxLength(200);
            b.Property(a => a.Notes).HasMaxLength(1000);
        });

        // 8. Multi-agent workflow (B6): runs, steps, approval audit, persisted drafts
        modelBuilder.Entity<WorkflowRun>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.UserId).IsRequired().HasMaxLength(100);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(50);
            b.Property(r => r.TotalCostUsd).HasPrecision(18, 6);
            b.Property(r => r.CorrelationId);
        });

        modelBuilder.Entity<AgentStep>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Role).HasConversion<string>().HasMaxLength(50);
            b.Property(s => s.Status).HasConversion<string>().HasMaxLength(50);
            b.Property(s => s.ToolName).HasMaxLength(100);
            b.Property(s => s.InputSummary).HasColumnType("text");
            b.Property(s => s.OutputSummary).HasColumnType("text");
            b.Property(s => s.CostUsd).HasPrecision(18, 6);
            b.Property(s => s.CorrelationId);
            b.HasOne<WorkflowRun>()
                .WithMany()
                .HasForeignKey(s => s.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(s => new { s.RunId, s.Order });
            b.HasIndex(s => s.CorrelationId);
        });

        modelBuilder.Entity<ApprovalAudit>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Decision).HasConversion<string>().HasMaxLength(50);
            b.Property(a => a.ApproverId).HasMaxLength(100);
            b.Property(a => a.Reason).HasColumnType("text");
            b.Property(a => a.ModifiedDraftJson).HasColumnType("text");
            b.HasOne<WorkflowRun>()
                .WithMany()
                .HasForeignKey(a => a.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(a => new { a.RunId, a.CreatedAtUtc }).IsDescending(false, true);
        });

        modelBuilder.Entity<PersistedDraft>(b =>
        {
            b.HasKey(d => d.Id);
            b.Property(d => d.DraftJson).HasColumnType("text");
            b.HasOne<WorkflowRun>()
                .WithMany()
                .HasForeignKey(d => d.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(d => d.RunId);
        });
    }
}
