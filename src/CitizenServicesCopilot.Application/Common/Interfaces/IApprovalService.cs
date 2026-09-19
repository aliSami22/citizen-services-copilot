using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Single source of truth for human approval decisions. Implementations write
/// through <see cref="IApprovalRecordRepository"/> and guard against double
/// decisions (idempotency).
/// </summary>
public interface IApprovalService
{
    Task<ApprovalRecord> ApproveAsync(string runId, string approverId, CancellationToken ct = default);

    Task<ApprovalRecord> RejectAsync(string runId, string approverId, string reason, CancellationToken ct = default);

    Task<ApprovalRecord> EditAndApproveAsync(
        string runId, string approverId, string editedDraftJson, string? reason, CancellationToken ct = default);

    Task<ApprovalRecord?> GetForRunAsync(string runId, CancellationToken ct = default);
}