namespace CitizenServicesCopilot.Domain.Agents;

public sealed record EligibilityResult(
    string Summary,
    int TokensUsed);