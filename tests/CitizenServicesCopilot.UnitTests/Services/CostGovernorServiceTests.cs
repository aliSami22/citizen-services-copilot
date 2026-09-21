using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.UnitTests.Services;

public class CostGovernorServiceTests
{
    private sealed class InMemoryBudgetRepository : IUserBudgetRepository
    {
        public Task<UserBudget?> GetByUserIdAsync(string userId, CancellationToken ct = default)
            => Task.FromResult<UserBudget?>(null);

        public Task AddAsync(UserBudget budget, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(UserBudget budget, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static CostGovernorService CreateGovernor(OrchestratorOptions options)
        => new(new InMemoryBudgetRepository(), options);

    private static OrchestratorOptions GeminiRoutingOptions() => new()
    {
        Routing = new RoutingOptions
        {
            CheapModel = "gemini-cheap-test",
            PremiumModel = "gemini-premium-test"
        }
    };

    [Fact]
    public async Task EvaluateAndEnforceBudgetAsync_SimpleQuestion_RoutesToConfiguredCheapModel()
    {
        var governor = CreateGovernor(GeminiRoutingOptions());

        var result = await governor.EvaluateAndEnforceBudgetAsync("citizen-1", "How do I renew my ID card?");

        Assert.True(result.IsAllowed);
        Assert.Equal("cheap", result.ModelTier);
        Assert.Equal("gemini-cheap-test", result.ModelName);
    }

    [Fact]
    public async Task EvaluateAndEnforceBudgetAsync_ComplexQuestion_RoutesToConfiguredPremiumModel()
    {
        var governor = CreateGovernor(GeminiRoutingOptions());
        // > 25 words triggers the complex tier.
        var longQuestion = string.Join(" ", Enumerable.Repeat("word", 30));

        var result = await governor.EvaluateAndEnforceBudgetAsync("citizen-1", longQuestion);

        Assert.True(result.IsAllowed);
        Assert.Equal("expensive", result.ModelTier);
        Assert.Equal("gemini-premium-test", result.ModelName);
    }

    [Fact]
    public async Task EvaluateAndEnforceBudgetAsync_CreatesDefaultBudgetForNewUser()
    {
        var governor = CreateGovernor(GeminiRoutingOptions());

        var result = await governor.EvaluateAndEnforceBudgetAsync("citizen-1", "How do I renew my ID card?");

        Assert.False(result.EstimatedCostUsd <= 0m);
    }
}