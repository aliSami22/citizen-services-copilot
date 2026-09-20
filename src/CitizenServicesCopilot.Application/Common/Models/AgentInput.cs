using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.Application.Common.Models;

public sealed record AgentInput(
    Guid RunId,
    string UserId,
    string Query,
    string ModelName,
    IReadOnlyList<DocumentChunk> ContextChunks,
    IReadOnlyList<AgentStep> PriorSteps);