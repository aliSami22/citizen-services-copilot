using CitizenServicesCopilot.Domain.Agents;

namespace CitizenServicesCopilot.Domain.Workflows;

/// <summary>
/// A single persisted stage/step of a workflow run. Role, status, summaries
/// and token accounting describe what the step did; Id, RunId, Order and
/// CorrelationId are persistence concerns assigned by the orchestrator / repository.
/// </summary>
public sealed record AgentStep(
    AgentRole Role,
    AgentStepStatus Status,
    DateTimeOffset CreatedAtUtc,
    string? OutputSummary = null,
    string? ErrorMessage = null,
    string? ToolName = null,
    string? InputSummary = null,
    int? TokensIn = null,
    int? TokensOut = null,
    decimal CostUsd = 0m,
    int? DurationMs = null,
    Guid Id = default,
    Guid RunId = default,
    int Order = 0,
    Guid? CorrelationId = null);