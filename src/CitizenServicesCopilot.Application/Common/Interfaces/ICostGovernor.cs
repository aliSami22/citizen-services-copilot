using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

public interface ICostGovernor
{
    Task<CostEstimationResult> EvaluateAndEnforceBudgetAsync(string userId, string question, CancellationToken ct = default);
    decimal CalculateActualCost(string modelTier, int promptTokens, int completionTokens);
}
