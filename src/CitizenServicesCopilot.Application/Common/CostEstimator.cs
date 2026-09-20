namespace CitizenServicesCopilot.Application.Common;

/// <summary>
/// Token-based cost math shared by the budget pre-flight check and the cost
/// governor. Rates are per 1,000 tokens (USD) and mirror the prices hard-coded
/// in <c>CostGovernorService</c>; keeping them in one place avoids drift.
/// </summary>
public static class CostEstimator
{
    public const decimal CheapInputRatePer1k = 0.00015m;
    public const decimal CheapOutputRatePer1k = 0.00060m;

    public const decimal ExpensiveInputRatePer1k = 0.00250m;
    public const decimal ExpensiveOutputRatePer1k = 0.01000m;

    /// <summary>
    /// Rough pre-flight estimate that splits the token budget evenly between
    /// prompt and completion.
    /// </summary>
    public static decimal EstimateCost(int estimatedTokens, bool cheap = true)
    {
        var inputTokens = estimatedTokens / 2;
        var outputTokens = estimatedTokens - inputTokens;
        var inputRate = cheap ? CheapInputRatePer1k : ExpensiveInputRatePer1k;
        var outputRate = cheap ? CheapOutputRatePer1k : ExpensiveOutputRatePer1k;
        return (inputTokens / 1000.0m * inputRate) + (outputTokens / 1000.0m * outputRate);
    }

    public static decimal CalculateActualCost(bool cheap, int promptTokens, int completionTokens)
    {
        var inputRate = cheap ? CheapInputRatePer1k : ExpensiveInputRatePer1k;
        var outputRate = cheap ? CheapOutputRatePer1k : ExpensiveOutputRatePer1k;
        return (promptTokens / 1000.0m * inputRate) + (completionTokens / 1000.0m * outputRate);
    }

    /// <summary>
    /// Heuristic tier for a routed model name: the cheap tier is "gpt-4o-mini"
    /// and the Ollama cheap model "llama3.2*"; everything else is premium.
    /// </summary>
    public static bool IsCheapModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return true;
        }

        return modelName.Contains("mini", StringComparison.OrdinalIgnoreCase)
            || modelName.StartsWith("llama3.2", StringComparison.OrdinalIgnoreCase)
            || modelName.Equals("cheap", StringComparison.OrdinalIgnoreCase);
    }
}