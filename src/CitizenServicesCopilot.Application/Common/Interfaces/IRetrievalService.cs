using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Provider-independent contract for hybrid document retrieval and evidence grounding.
/// </summary>
public interface IRetrievalService
{
    /// <summary>
    /// Executes hybrid retrieval (dense + keyword fusion) and returns scored chunks or grounded refusal.
    /// </summary>
    Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default);
}
