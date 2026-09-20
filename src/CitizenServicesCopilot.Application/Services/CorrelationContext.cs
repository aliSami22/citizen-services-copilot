using CitizenServicesCopilot.Application.Common.Interfaces;

namespace CitizenServicesCopilot.Application.Services;

public class CorrelationContext : ICorrelationContext
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
}