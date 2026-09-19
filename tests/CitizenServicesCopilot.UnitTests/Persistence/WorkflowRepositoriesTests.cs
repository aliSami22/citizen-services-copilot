using CitizenServicesCopilot.Domain.Agents;
using CitizenServicesCopilot.Domain.Workflows;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Infrastructure.Persistence;
using CitizenServicesCopilot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CitizenServicesCopilot.UnitTests.Persistence;

/// <summary>
/// Offline EF Core integration tests for the multi-agent workflow repositories
/// (workflow runs, agent steps, approval audits, persisted drafts) using an
/// in-memory AppDbContext subset that omits the pgvector extension, following
/// the GroundedRetrieverTests pattern.
/// </summary>
public class WorkflowRepositoriesTests
{
    private readonly DbContextOptions<AppDbContext> _inMemoryOptions;
    private readonly string _databaseName;

    public WorkflowRepositoriesTests()
    {
        _databaseName = "wf-repos-" + Guid.NewGuid().ToString("N");
        _inMemoryOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;
    }


    private InMemoryWorkflowDbContext BuildDb()
        => new(_inMemoryOptions);

    [Fact]
    public async Task WorkflowRun_RoundTripsAllFields()
    {
        await using var db = BuildDb();
        var repo = new EfWorkflowRunRepository(db);
        var run = NewRun();

        await repo.AddAsync(run);

        var loaded = await repo.GetByIdAsync(run.Id);
        Assert.NotNull(loaded);
        Assert.Equal(run.UserId, loaded!.UserId);
        Assert.Equal(run.Status, loaded.Status);
        Assert.Equal(run.StartedAtUtc, loaded.StartedAtUtc);
        Assert.Equal(run.CompletedAtUtc, loaded.CompletedAtUtc);
        Assert.Equal(run.TotalCostUsd, loaded.TotalCostUsd);
        Assert.Equal(run.ErrorMessage, loaded.ErrorMessage);
    }

    [Fact]
    public async Task WorkflowRun_AddTwice_UpsertsInsteadOfDuplicating()
    {
        await using var db = BuildDb();
        var repo = new EfWorkflowRunRepository(db);
        var run = NewRun();
        await repo.AddAsync(run);

        var updated = run with { Status = RunStatus.Completed, TotalCostUsd = 12.5m };
        await repo.AddAsync(updated);

        Assert.Equal(1, await db.WorkflowRuns.CountAsync());
        var loaded = await repo.GetByIdAsync(run.Id);
        Assert.Equal(RunStatus.Completed, loaded!.Status);
        Assert.Equal(12.5m, loaded.TotalCostUsd);
    }

    [Fact]
    public async Task AgentStep_GetForRun_ReturnsOrderedByOrder()
    {
        await using var db = BuildDb();
        var run = NewRun();
        await new EfWorkflowRunRepository(db).AddAsync(run);
        var repo = new EfAgentStepRepository(db);
        var runId = run.Id;
        var steps = new[]
        {
            NewStep(AgentRole.ProcedureResolver, 3),
            NewStep(AgentRole.EligibilityIdentifier, 1),
            NewStep(AgentRole.ResponseDrafter, 2)
        };
        foreach (var s in steps) await repo.AddAsync(s with { RunId = runId });

        var loaded = await repo.GetForRunAsync(runId);

        Assert.Equal(3, loaded.Count);
        Assert.Equal(new[] { 1, 2, 3 }, loaded.Select(s => s.Order).ToArray());
        Assert.All(loaded, s => Assert.Equal(runId, s.RunId));
    }

    [Fact]
    public async Task ApprovalAudit_AddTwice_UpsertsByRun()
    {
        await using var db = BuildDb();
        var run = NewRun();
        await new EfWorkflowRunRepository(db).AddAsync(run);
        var repo = new EfApprovalRecordRepository(db);
        var runId = run.Id;
        var audit = new ApprovalAudit(
            Guid.NewGuid(), runId, ApprovalDecision.Approved, DateTimeOffset.UtcNow, "officer-1", null, null);

        await repo.AddAsync(audit);
        await repo.AddAsync(audit with { ApproverId = "officer-2" });

        Assert.Equal(1, await db.ApprovalAudits.CountAsync());
        var loaded = await repo.GetForRunAsync(runId);
        Assert.Equal("officer-2", loaded!.ApproverId);
    }

    [Fact]
    public async Task PersistedDraft_PersistTwice_AppendsUniqueRows()
    {
        await using var db = BuildDb();
        var run = NewRun();
        await new EfWorkflowRunRepository(db).AddAsync(run);
        var repo = new EfPersistedDraftRepository(db);
        var runId = run.Id;

        await repo.PersistAsync(runId, "{\"draft\":\"v1\"}");
        await repo.PersistAsync(runId, "{\"draft\":\"v2\"}");

        var loaded = await db.PersistedDrafts.Where(d => d.RunId == runId).ToListAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Equal(new[] { "{\"draft\":\"v1\"}", "{\"draft\":\"v2\"}" }, loaded.Select(d => d.DraftJson).ToArray());
    }

    private static WorkflowRun NewRun()
        => new(Guid.NewGuid(), "user-9", RunStatus.Running, DateTimeOffset.UtcNow, null, 0m, null);

    private static AgentStep NewStep(AgentRole role, int order)
        => new(Role: role, Status: AgentStepStatus.Succeeded, CreatedAtUtc: DateTimeOffset.UtcNow,
            OutputSummary: "ok", ErrorMessage: null, DurationMs: 88, Order: order);

    private sealed class InMemoryWorkflowDbContext : AppDbContext
    {
        public InMemoryWorkflowDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<WorkflowRun>(b =>
            {
                b.HasKey(r => r.Id);
                b.Property(r => r.UserId).IsRequired().HasMaxLength(100);
                b.Property(r => r.Status).HasConversion<string>().HasMaxLength(50);
                b.Property(r => r.TotalCostUsd).HasPrecision(18, 6);
            });

            modelBuilder.Entity<AgentStep>(b =>
            {
                b.HasKey(s => s.Id);
                b.Property(s => s.Role).HasConversion<string>().HasMaxLength(50);
                b.Property(s => s.Status).HasConversion<string>().HasMaxLength(50);
                b.HasOne<WorkflowRun>()
                    .WithMany()
                    .HasForeignKey(s => s.RunId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(s => new { s.RunId, s.Order });
            });

            modelBuilder.Entity<ApprovalAudit>(b =>
            {
                b.HasKey(a => a.Id);
                b.Property(a => a.Decision).HasConversion<string>().HasMaxLength(50);
                b.HasOne<WorkflowRun>()
                    .WithMany()
                    .HasForeignKey(a => a.RunId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<InquiryDraft>(b =>
            {
                b.HasKey(d => d.Id);
                b.Ignore(d => d.Citations);
            });

            modelBuilder.Entity<PersistedDraft>(b =>
            {
                b.HasKey(d => d.Id);
                b.HasOne<WorkflowRun>()
                    .WithMany()
                    .HasForeignKey(d => d.RunId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}

