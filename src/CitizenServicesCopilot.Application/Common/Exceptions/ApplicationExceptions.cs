namespace CitizenServicesCopilot.Application.Common.Exceptions;

public class BudgetExceededException : Exception
{
    public string UserId { get; }
    public decimal Spent { get; }
    public decimal Required { get; }
    public decimal Budget { get; }

    public BudgetExceededException(string userId, decimal spent, decimal required, decimal budget)
        : base($"Budget exceeded for user '{userId}'. Spent: ${spent:F4}, Estimated Needed: ${required:F4}, Total Budget: ${budget:F4}. Inquiry blocked.")
    {
        UserId = userId;
        Spent = spent;
        Required = required;
        Budget = budget;
    }
}

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entityName, object key)
        : base($"{entityName} with key '{key}' was not found.")
    {
    }
}
