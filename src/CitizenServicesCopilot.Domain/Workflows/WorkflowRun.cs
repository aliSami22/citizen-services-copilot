namespace CitizenServicesCopilot.Domain.Workflows;

/// <summary>
/// Aggregated record of a single multi-agent workflow execution.
/// </summary>
public sealed record WorkflowRun(
    Guid Id,
    string UserId,
    RunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    decimal TotalCostUsd,
    string? ErrorMessage)
{
    public static WorkflowRun Create(string userId) => new(
        Id: Guid.NewGuid(),
        UserId: userId,
        Status: RunStatus.NotStarted,
        StartedAtUtc: DateTimeOffset.UtcNow,
        CompletedAtUtc: null,
        TotalCostUsd: 0m,
        ErrorMessage: null);
}