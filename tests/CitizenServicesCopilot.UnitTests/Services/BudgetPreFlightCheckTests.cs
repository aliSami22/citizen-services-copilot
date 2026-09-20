using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.UnitTests.Services;

public class BudgetPreFlightCheckTests
{
    [Fact]
    public async Task CheckAsync_NoBudgetRecord_ReturnsAllowed()
    {
        var check = new BudgetPreFlightCheck(new StubBudgetRepository(null));

        var result = await check.CheckAsync("user-x", estimatedTokens: 1000);

        Assert.Equal(BudgetCheckResult.Allowed, result);
    }

    [Fact]
    public async Task CheckAsync_UnderBudget_ReturnsAllowed()
    {
        var budget = new UserBudget
        {
            UserId = "user-1",
            AllocatedBudgetUsd = 10m,
            SpentUsd = 0m,
            IsBlocked = false
        };
        var check = new BudgetPreFlightCheck(new StubBudgetRepository(budget));

        var result = await check.CheckAsync("user-1", estimatedTokens: 1000);

        Assert.Equal(BudgetCheckResult.Allowed, result);
    }

    [Fact]
    public async Task CheckAsync_BlockedUser_ReturnsDenied()
    {
        var budget = new UserBudget
        {
            UserId = "user-1",
            AllocatedBudgetUsd = 10m,
            SpentUsd = 1m,
            IsBlocked = true
        };
        var check = new BudgetPreFlightCheck(new StubBudgetRepository(budget));

        var result = await check.CheckAsync("user-1", estimatedTokens: 1000);

        Assert.Equal(BudgetCheckResult.Denied, result);
    }

    [Fact]
    public async Task CheckAsync_EstimatedCostExceedsRemaining_ReturnsWouldExceed()
    {
        // remaining budget (0.0001) < estimated cost (0.000375 for 1000 tokens)
        var budget = new UserBudget
        {
            UserId = "user-1",
            AllocatedBudgetUsd = 0.0001m,
            SpentUsd = 0m,
            IsBlocked = false
        };
        var check = new BudgetPreFlightCheck(new StubBudgetRepository(budget));

        var result = await check.CheckAsync("user-1", estimatedTokens: 1000);

        Assert.Equal(BudgetCheckResult.WouldExceed, result);
    }

    private sealed class StubBudgetRepository : IUserBudgetRepository
    {
        private readonly UserBudget? _budget;

        public StubBudgetRepository(UserBudget? budget) => _budget = budget;

        public Task<UserBudget?> GetByUserIdAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(_budget);

        public Task AddAsync(UserBudget budget, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(UserBudget budget, CancellationToken ct = default) => Task.CompletedTask;
    }
}