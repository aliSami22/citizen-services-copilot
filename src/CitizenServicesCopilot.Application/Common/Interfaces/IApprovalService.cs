using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Single source of truth for human approval decisions. Implementations write
/// through <see cref="IApprovalRecordRepository"/> and guard against double
/// decisions (idempotency).
/// </summary>
public interface IApprovalService
{
    Task<ApprovalAudit> ApproveAsync(string runId, string approverId, CancellationToken ct = default);

    Task<ApprovalAudit> RejectAsync(string runId, string approverId, string reason, CancellationToken ct = default);

    Task<ApprovalAudit> EditAndApproveAsync(
        string runId, string approverId, string editedDraftJson, string? reason, CancellationToken ct = default);

    Task<ApprovalAudit?> GetForRunAsync(string runId, CancellationToken ct = default);
}