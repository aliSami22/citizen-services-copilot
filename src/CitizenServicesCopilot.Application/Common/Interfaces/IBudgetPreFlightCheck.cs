using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Budget gate evaluated before the first agent stage and again between stages.
/// Returns <see cref="BudgetCheckResult.Allowed"/> for users without a budget
/// record (unrestricted), <see cref="BudgetCheckResult.Denied"/> for hard-blocked
/// users, and <see cref="BudgetCheckResult.WouldExceed"/> when the estimated cost
/// exceeds the remaining budget.
/// </summary>
public interface IBudgetPreFlightCheck
{
    Task<BudgetCheckResult> CheckAsync(string userId, int estimatedTokens, CancellationToken ct = default);
}