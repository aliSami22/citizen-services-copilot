namespace CitizenServicesCopilot.Domain.Workflows;

/// <summary>
/// Durable snapshot of an approved draft, written exactly once after a run
/// passes its approval gate.
/// </summary>
public sealed record PersistedDraft(
    Guid Id,
    Guid RunId,
    string DraftJson,
    DateTimeOffset PersistedAtUtc);