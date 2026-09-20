using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace CitizenServicesCopilot.Infrastructure.Persistence.Repositories;

public class EfWorkflowRunRepository : IWorkflowRunRepository
{
    private readonly AppDbContext _context;

    public EfWorkflowRunRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<WorkflowRun?> GetByIdAsync(Guid runId, CancellationToken ct = default)
        => await _context.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);

    public async Task<IReadOnlyList<WorkflowRun>> GetByUserIdAsync(string userId, CancellationToken ct = default)
        => await _context.WorkflowRuns
            .Where(r => r.UserId == userId)
            .ToListAsync(ct);

    public async Task AddAsync(WorkflowRun run, CancellationToken ct = default)
    {
        // Idempotent add: a repeated AddAsync for the same run updates it instead
        // of throwing on the PK (matches the in-memory contract the orchestrator
        // tests rely on).
        // GetByIdAsync materialises (tracks) the existing row, so blindly calling
        // Update(otherInstance) would fail with "another instance with the same
        // key is already being tracked". Instead we adopt the caller's values
        // onto the already-tracked instance — this is what makes a repeated
        // AddAsync an idempotent upsert under a long-lived (test) context.
        var existing = await GetByIdAsync(run.Id, ct);
        if (existing is not null)
        {
            _context.Entry(existing).CurrentValues.SetValues(run);
        }
        else
        {
            await _context.WorkflowRuns.AddAsync(run, ct);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(WorkflowRun run, CancellationToken ct = default)
    {
        // Tracking-friendly upsert, same rationale as AddAsync: the orchestrator
        // mutates runs with record `with` expressions, so every Update is a *new*
        // instance. Update(newInstance) would fail with "another instance with the
        // same key is already being tracked" under a shared context; instead adopt
        // the caller's values onto the already-tracked row.
        var existing = await GetByIdAsync(run.Id, ct);
        if (existing is not null)
        {
            _context.Entry(existing).CurrentValues.SetValues(run);
        }
        else
        {
            await _context.WorkflowRuns.AddAsync(run, ct);
        }

        await _context.SaveChangesAsync(ct);
    }
}

public class EfAgentStepRepository : IAgentStepRepository
{
    private readonly AppDbContext _context;

    public EfAgentStepRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AgentStep step, CancellationToken ct = default)
    {
        if (step.Id == Guid.Empty)
        {
            step = step with { Id = Guid.NewGuid() };
        }

        await _context.AgentSteps.AddAsync(step, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AgentStep>> GetForRunAsync(Guid runId, CancellationToken ct = default)
        => await _context.AgentSteps
            .Where(s => s.RunId == runId)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);
}

public class EfApprovalRecordRepository : IApprovalRecordRepository
{
    private readonly AppDbContext _context;

    public EfApprovalRecordRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ApprovalAudit?> GetForRunAsync(Guid runId, CancellationToken ct = default)
        => await _context.ApprovalAudits
            .Where(a => a.RunId == runId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(ApprovalAudit audit, CancellationToken ct = default)
    {
        // A run has at most one decision (IApprovalService enforces it); the
        // repository makes repeated writes idempotent by overwriting.
        // Same tracking-friendly upsert as the run repository: adopt values onto
        // the already-tracked row instead of Update(copy), which in-memory rejects
        // when the row is still being tracked under a shared context.
        var existing = await GetForRunAsync(audit.RunId, ct);
        if (existing is not null)
        {
            _context.Entry(existing).CurrentValues.SetValues(audit);
        }
        else
        {
            await _context.ApprovalAudits.AddAsync(audit, ct);
        }

        await _context.SaveChangesAsync(ct);
    }
}

public class EfPersistedDraftRepository : IPersistedDraftRepository
{
    private readonly AppDbContext _context;

    public EfPersistedDraftRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task PersistAsync(Guid runId, string draftJson, CancellationToken ct = default)
    {
        var draft = new PersistedDraft(
            Id: Guid.NewGuid(),
            RunId: runId,
            DraftJson: draftJson,
            PersistedAtUtc: DateTimeOffset.UtcNow);

        await _context.PersistedDrafts.AddAsync(draft, ct);
        await _context.SaveChangesAsync(ct);
    }
}