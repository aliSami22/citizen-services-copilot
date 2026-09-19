using CitizenServicesCopilot.Domain.Agents;

namespace CitizenServicesCopilot.Domain.Workflows;

public sealed record AgentStep(
    AgentRole Role,
    AgentStepStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int? TokensUsed,
    string? OutputSummary,
    string? ErrorMessage);