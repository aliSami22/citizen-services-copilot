using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Agents;

namespace CitizenServicesCopilot.Application.Services;

/// <summary>
/// Configuration-driven model router. Reads <c>Orchestrator:Routing</c> from
/// configuration (CheapModel default "gpt-4o-mini", PremiumModel default
/// "gpt-4o") and maps eligibility/procedure stages to the cheap model and the
/// response drafter to the premium model.
/// </summary>
public class ConfigurationModelRouter : IModelRouter
{
    private readonly OrchestratorOptions _options;

    public ConfigurationModelRouter(OrchestratorOptions options)
    {
        _options = options;
    }

    public string SelectModel(AgentRole role) =>
        role == AgentRole.ResponseDrafter
            ? _options.Routing.PremiumModel
            : _options.Routing.CheapModel;
}