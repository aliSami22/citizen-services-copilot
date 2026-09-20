using CitizenServicesCopilot.Application.Common;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services;

/// <summary>
/// Real budget gate wired at Checkpoint C. Users without a budget record are
/// unrestricted (returns <see cref="BudgetCheckResult.Allowed"/> — the former
/// no-op fallback behaviour). A blocked record is <see cref="BudgetCheckResult.Denied"/>;
/// an estimate that would exceed the remaining budget is <see cref="BudgetCheckResult.WouldExceed"/>.
/// </summary>
public class BudgetPreFlightCheck : IBudgetPreFlightCheck
{
    private readonly IUserBudgetRepository _budgetRepository;

    public BudgetPreFlightCheck(IUserBudgetRepository budgetRepository)
    {
        _budgetRepository = budgetRepository;
    }

    public async Task<BudgetCheckResult> CheckAsync(string userId, int estimatedTokens, CancellationToken ct = default)
    {
        var budget = await _budgetRepository.GetByUserIdAsync(userId, ct);
        if (budget is null)
        {
            // No budget record -> unrestricted.
            return BudgetCheckResult.Allowed;
        }

        if (budget.IsBlocked)
        {
            return BudgetCheckResult.Denied;
        }

        var estimatedCost = CostEstimator.EstimateCost(estimatedTokens, cheap: true);
        return budget.CanAfford(estimatedCost)
            ? BudgetCheckResult.Allowed
            : BudgetCheckResult.WouldExceed;
    }
}