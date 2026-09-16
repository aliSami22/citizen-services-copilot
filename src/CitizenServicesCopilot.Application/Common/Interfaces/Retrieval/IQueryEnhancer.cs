namespace CitizenServicesCopilot.Application.Common.Interfaces.Retrieval;

/// <summary>
/// Enhances and rewrites raw citizen queries into canonical domain terms for improved retrieval.
/// </summary>
public interface IQueryEnhancer
{
    /// <summary>
    /// Normalizes and enriches the input query with canonical regulatory terminology.
    /// </summary>
    Task<string> EnhanceQueryAsync(string query, CancellationToken ct = default);
}
