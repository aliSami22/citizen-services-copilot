namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Budget gate evaluated once before a workflow run starts. A concrete cost
/// governor is wired in Checkpoint C; the placeholder keeps the orchestrator's
/// contract explicit.
/// </summary>
public interface IBudgetPreFlightCheck
{
    /// <summary>
    /// Throws <see cref="Application.Common.Exceptions.BudgetExceededException"/>
    /// (or a budget-specific exception) when the user may not start a run.
    /// </summary>
    Task ThrowIfExceededAsync(string userId, CancellationToken ct = default);
}