using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services;
using CitizenServicesCopilot.Domain.Agents;

namespace CitizenServicesCopilot.UnitTests.Services;

public class ConfigurationModelRouterTests
{
    [Fact]
    public void SelectModel_EligibilityAndProcedure_ReturnsCheapModel()
    {
        var router = new ConfigurationModelRouter(new OrchestratorOptions());

        Assert.Equal("gpt-4o-mini", router.SelectModel(AgentRole.EligibilityIdentifier));
        Assert.Equal("gpt-4o-mini", router.SelectModel(AgentRole.ProcedureResolver));
    }

    [Fact]
    public void SelectModel_ResponseDrafter_ReturnsPremiumModel()
    {
        var router = new ConfigurationModelRouter(new OrchestratorOptions());

        Assert.Equal("gpt-4o", router.SelectModel(AgentRole.ResponseDrafter));
    }
}