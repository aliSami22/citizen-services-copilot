namespace CitizenServicesCopilot.Application.Common.Models;

/// <summary>
/// Outcome of a budget pre-flight evaluation.
/// </summary>
public enum BudgetCheckResult
{
    /// <summary>
    /// The user may proceed. Either no budget record exists (unrestricted) or
    /// the estimated cost fits within the remaining budget.
    /// </summary>
    Allowed = 0,

    /// <summary>
    /// The user is hard-blocked (for example the budget record is flagged as blocked).
    /// </summary>
    Denied,

    /// <summary>
    /// The estimated cost would exceed the user's remaining budget.
    /// </summary>
    WouldExceed
}