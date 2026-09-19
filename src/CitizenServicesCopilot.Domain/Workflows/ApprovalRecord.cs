using System;

namespace CitizenServicesCopilot.Domain.Workflows;

public sealed record ApprovalRecord(
    Guid RunId,
    ApprovalDecision Decision,
    DateTimeOffset DecidedAtUtc,
    string? ApprovedBy,
    string? Notes,
    string? ModifiedDraftJson);