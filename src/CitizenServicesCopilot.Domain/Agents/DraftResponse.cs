using CitizenServicesCopilot.Domain.ValueObjects;

namespace CitizenServicesCopilot.Domain.Agents;

public sealed record DraftResponse(
    bool IsRefusal,
    string? RefusalReason,
    string EligibilitySummary,
    string RequiredDocuments,
    string ProcedureSteps,
    string FeesAndTimeline,
    IReadOnlyList<Citation> Citations,
    int TokensUsed);