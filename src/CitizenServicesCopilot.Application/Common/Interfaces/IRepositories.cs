using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

public interface IInquiryRepository
{
    Task<Inquiry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Inquiry>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task AddAsync(Inquiry inquiry, CancellationToken ct = default);
    Task UpdateAsync(Inquiry inquiry, CancellationToken ct = default);
}

public interface IUserBudgetRepository
{
    Task<UserBudget?> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task AddAsync(UserBudget budget, CancellationToken ct = default);
    Task UpdateAsync(UserBudget budget, CancellationToken ct = default);
}
