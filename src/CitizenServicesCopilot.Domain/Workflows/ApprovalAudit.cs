namespace CitizenServicesCopilot.Domain.Workflows;

/// <summary>
/// Audit trail entry recording a single human approval decision for a
/// workflow run. A run has at most one decision (enforced by
/// IApprovalService idempotency and the approval repository contract).
/// </summary>
public sealed record ApprovalAudit(
    Guid Id,
    Guid RunId,
    ApprovalDecision Decision,
    DateTimeOffset CreatedAtUtc,
    string? ApproverId,
    string? Reason,
    string? ModifiedDraftJson);