namespace CitizenServicesCopilot.Domain.Entities;

public class UserBudget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public decimal AllocatedBudgetUsd { get; set; }
    public decimal SpentUsd { get; set; }
    public int TotalTokensUsed { get; set; }
    public bool IsBlocked { get; set; }

    public bool CanAfford(decimal estimatedCost)
    {
        if (IsBlocked) return false;
        return (SpentUsd + estimatedCost) <= AllocatedBudgetUsd;
    }

    public void RecordUsage(int tokens, decimal cost)
    {
        TotalTokensUsed += tokens;
        SpentUsd += cost;

        if (SpentUsd >= AllocatedBudgetUsd)
        {
            IsBlocked = true;
        }
    }
}
