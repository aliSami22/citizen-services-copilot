namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Durable destination for an approved draft. EF Core implementation lands in
/// Checkpoint B6.
/// </summary>
public interface IPersistedDraftRepository
{
    Task PersistAsync(Guid runId, string draftJson, CancellationToken ct = default);
}