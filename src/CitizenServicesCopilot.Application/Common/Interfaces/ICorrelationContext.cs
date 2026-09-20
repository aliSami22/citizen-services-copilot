namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Scoped correlation context propagated from the HTTP request through the
/// orchestrator, the agents and down to each LLM call.
/// </summary>
public interface ICorrelationContext
{
    Guid CorrelationId { get; set; }
}