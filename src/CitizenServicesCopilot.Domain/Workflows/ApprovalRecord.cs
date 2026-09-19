namespace CitizenServicesCopilot.Domain.Workflows;

public sealed record ApprovalRecord(
    ApprovalDecision Decision,
    DateTimeOffset DecidedAtUtc,
    string? ApprovedBy,
    string? Notes,
    string? ModifiedDraftJson);