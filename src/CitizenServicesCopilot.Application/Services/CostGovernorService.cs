using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Services;

public class CostGovernorService : ICostGovernor
{
    private readonly IUserBudgetRepository _budgetRepository;

    // Rates per 1,000 tokens (USD)
    public const decimal CheapInputRatePer1k = 0.00015m;
    public const decimal CheapOutputRatePer1k = 0.00060m;

    public const decimal ExpensiveInputRatePer1k = 0.00250m;
    public const decimal ExpensiveOutputRatePer1k = 0.01000m;

    public CostGovernorService(IUserBudgetRepository budgetRepository)
    {
        _budgetRepository = budgetRepository;
    }

    public async Task<CostEstimationResult> EvaluateAndEnforceBudgetAsync(string userId, string question, CancellationToken ct = default)
    {
        var budget = await _budgetRepository.GetByUserIdAsync(userId, ct);
        if (budget == null)
        {
            // Default initial budget for new users: $0.50 USD
            budget = new UserBudget
            {
                UserId = userId,
                AllocatedBudgetUsd = 0.50m,
                SpentUsd = 0.0m,
                TotalTokensUsed = 0,
                IsBlocked = false
            };
            await _budgetRepository.AddAsync(budget, ct);
        }

        // 1. Classify Complexity: Simple vs Complex
        bool isComplex = IsQueryComplex(question);
        string modelTier = isComplex ? "expensive" : "cheap";
        string modelName = isComplex ? "gpt-4o" : "gpt-4o-mini";

        // 2. Estimate Tokens BEFORE LLM Call
        // Approximate token count: 1 token ~ 4 chars + agent prompt overhead + retrieved context
        int questionTokens = Math.Max(10, question.Length / 4);
        int estimatedContextTokens = 700; // Expected retrieved chunks & system prompt
        int estimatedPromptTokens = questionTokens + estimatedContextTokens;
        int estimatedCompletionTokens = isComplex ? 450 : 250;

        // 3. Calculate Estimated Cost
        decimal inputRate = isComplex ? ExpensiveInputRatePer1k : CheapInputRatePer1k;
        decimal outputRate = isComplex ? ExpensiveOutputRatePer1k : CheapOutputRatePer1k;

        decimal estimatedCost = ((estimatedPromptTokens / 1000.0m) * inputRate)
                              + ((estimatedCompletionTokens / 1000.0m) * outputRate);

        // 4. Enforce Hard Cutoff
        if (!budget.CanAfford(estimatedCost))
        {
            throw new BudgetExceededException(userId, budget.SpentUsd, estimatedCost, budget.AllocatedBudgetUsd);
        }

        return new CostEstimationResult(
            ModelTier: modelTier,
            ModelName: modelName,
            EstimatedPromptTokens: estimatedPromptTokens,
            EstimatedCompletionTokens: estimatedCompletionTokens,
            EstimatedCostUsd: estimatedCost,
            IsAllowed: true
        );
    }

    public decimal CalculateActualCost(string modelTier, int promptTokens, int completionTokens)
    {
        bool isExpensive = modelTier.Equals("expensive", StringComparison.OrdinalIgnoreCase)
                        || modelTier.Contains("4o", StringComparison.OrdinalIgnoreCase) && !modelTier.Contains("mini", StringComparison.OrdinalIgnoreCase);

        decimal inputRate = isExpensive ? ExpensiveInputRatePer1k : CheapInputRatePer1k;
        decimal outputRate = isExpensive ? ExpensiveOutputRatePer1k : CheapOutputRatePer1k;

        return ((promptTokens / 1000.0m) * inputRate) + ((completionTokens / 1000.0m) * outputRate);
    }

    private static bool IsQueryComplex(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return false;

        // Multi-question or long query heuristics
        var wordCount = question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 25) return true;

        var sentences = question.Split(new[] { '?', '.', '!' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length > 2) return true;

        // Complex conditional / cross-regulatory keywords
        string[] complexKeywords = ["exception", "dispute", "appeal", "conflict", "legal", "exemption", "penalty", "court", "stolen and lost", "foreign national"];
        return complexKeywords.Any(k => question.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
