using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CitizenServicesCopilot.Infrastructure.Persistence.Repositories;

public class InquiryRepository : IInquiryRepository
{
    private readonly AppDbContext _context;

    public InquiryRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Inquiry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Inquiries
            .Include(i => i.Draft)
            .Include(i => i.AuditLogs)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<IReadOnlyList<Inquiry>> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        return await _context.Inquiries
            .Include(i => i.Draft)
            .Include(i => i.AuditLogs)
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Inquiry inquiry, CancellationToken ct = default)
    {
        await _context.Inquiries.AddAsync(inquiry, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Inquiry inquiry, CancellationToken ct = default)
    {
        _context.Inquiries.Update(inquiry);
        await _context.SaveChangesAsync(ct);
    }
}

public class UserBudgetRepository : IUserBudgetRepository
{
    private readonly AppDbContext _context;

    public UserBudgetRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UserBudget?> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        return await _context.UserBudgets
            .FirstOrDefaultAsync(u => u.UserId == userId, ct);
    }

    public async Task AddAsync(UserBudget budget, CancellationToken ct = default)
    {
        await _context.UserBudgets.AddAsync(budget, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(UserBudget budget, CancellationToken ct = default)
    {
        _context.UserBudgets.Update(budget);
        await _context.SaveChangesAsync(ct);
    }
}

public class DocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _context;

    public DocumentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<Document?> GetByContentHashAsync(string contentHash, CancellationToken ct = default)
    {
        return await _context.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.ContentHash == contentHash, ct);
    }

    public async Task AddAsync(Document document, CancellationToken ct = default)
    {
        await _context.Documents.AddAsync(document, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Document document, CancellationToken ct = default)
    {
        _context.Documents.Update(document);
        await _context.SaveChangesAsync(ct);
    }
}
