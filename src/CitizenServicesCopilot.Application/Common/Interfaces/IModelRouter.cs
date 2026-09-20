using CitizenServicesCopilot.Domain.Agents;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Picks the LLM model for a given agent role. Eligibility and procedure
/// resolution run on a cheap model; drafting is routed to a premium model.
/// </summary>
public interface IModelRouter
{
    string SelectModel(AgentRole role);
}