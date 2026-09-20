using CitizenServicesCopilot.Application.Common;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.Application.Services;

/// <summary>
/// Encapsulates human approval decisions for workflow runs. Every decision is
/// persisted through <see cref="IApprovalRecordRepository"/> and is idempotent:
/// a run may only ever receive one decision.
/// </summary>
public sealed class ApprovalService : IApprovalService
{
    private readonly IApprovalRecordRepository _repository;

    public ApprovalService(IApprovalRecordRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ApprovalAudit> ApproveAsync(string runId, string approverId, CancellationToken ct = default)
    {
        var run = ParseRunId(runId);
        await ThrowIfAlreadyDecidedAsync(run, ct);

        var audit = new ApprovalAudit(
            Id: Guid.NewGuid(),
            RunId: run,
            Decision: ApprovalDecision.Approved,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ApproverId: approverId,
            Reason: null,
            ModifiedDraftJson: null);

        await _repository.AddAsync(audit, ct);
        return audit;
    }

    public async Task<ApprovalAudit> RejectAsync(string runId, string approverId, string reason, CancellationToken ct = default)
    {
        var run = ParseRunId(runId);
        await ThrowIfAlreadyDecidedAsync(run, ct);

        var audit = new ApprovalAudit(
            Id: Guid.NewGuid(),
            RunId: run,
            Decision: ApprovalDecision.Rejected,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ApproverId: approverId,
            Reason: reason,
            ModifiedDraftJson: null);

        await _repository.AddAsync(audit, ct);
        return audit;
    }

    public async Task<ApprovalAudit> EditAndApproveAsync(
        string runId, string approverId, string editedDraftJson, string? reason, CancellationToken ct = default)
    {
        var run = ParseRunId(runId);
        await ThrowIfAlreadyDecidedAsync(run, ct);

        if (string.IsNullOrWhiteSpace(editedDraftJson))
        {
            throw new InvalidEditedContentException("edited content must not be empty");
        }

        DraftJsonShapeValidator.Validate(editedDraftJson);

        var audit = new ApprovalAudit(
            Id: Guid.NewGuid(),
            RunId: run,
            Decision: ApprovalDecision.EditedAndApproved,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ApproverId: approverId,
            Reason: reason,
            ModifiedDraftJson: editedDraftJson);

        await _repository.AddAsync(audit, ct);
        return audit;
    }

    public Task<ApprovalAudit?> GetForRunAsync(string runId, CancellationToken ct = default)
        => _repository.GetForRunAsync(ParseRunId(runId), ct);

    private async Task ThrowIfAlreadyDecidedAsync(Guid runId, CancellationToken ct)
    {
        var existing = await _repository.GetForRunAsync(runId, ct);
        if (existing is not null)
        {
            throw new ApprovalAlreadyDecidedException(runId, existing.Decision.ToString());
        }
    }

    private static Guid ParseRunId(string runId)
        => Guid.TryParse(runId, out var id)
            ? id
            : throw new ArgumentException($"'{runId}' is not a valid run identifier.", nameof(runId));
}