using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Agents;
using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

public interface IAgent
{
    AgentRole Role { get; }

    IReadOnlySet<string> AllowedTools { get; }

    Task<AgentStep> ExecuteAsync(AgentInput input, CancellationToken ct);
}