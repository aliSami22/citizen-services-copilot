namespace CitizenServicesCopilot.Application.Common.Models;

public record LlmMessage(string Role, string Content);

public record LlmPrompt(
    IReadOnlyList<LlmMessage> Messages,
    string ModelName,
    float Temperature = 0.1f,
    int MaxTokens = 1000
);

public record LlmResponse(
    string Content,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string ModelUsed
);

public record CostEstimationResult(
    string ModelTier, // "cheap" | "expensive"
    string ModelName,
    int EstimatedPromptTokens,
    int EstimatedCompletionTokens,
    decimal EstimatedCostUsd,
    bool IsAllowed
);
