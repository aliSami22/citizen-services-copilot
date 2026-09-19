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

    public async Task<ApprovalRecord> ApproveAsync(string runId, string approverId, CancellationToken ct = default)
    {
        var run = ParseRunId(runId);
        await ThrowIfAlreadyDecidedAsync(run, ct);

        var record = new ApprovalRecord(
            RunId: run,
            Decision: ApprovalDecision.Approved,
            DecidedAtUtc: DateTimeOffset.UtcNow,
            ApprovedBy: approverId,
            Notes: null,
            ModifiedDraftJson: null);

        await _repository.AddAsync(record, ct);
        return record;
    }

    public async Task<ApprovalRecord> RejectAsync(string runId, string approverId, string reason, CancellationToken ct = default)
    {
        var run = ParseRunId(runId);
        await ThrowIfAlreadyDecidedAsync(run, ct);

        var record = new ApprovalRecord(
            RunId: run,
            Decision: ApprovalDecision.Rejected,
            DecidedAtUtc: DateTimeOffset.UtcNow,
            ApprovedBy: approverId,
            Notes: reason,
            ModifiedDraftJson: null);

        await _repository.AddAsync(record, ct);
        return record;
    }

    public async Task<ApprovalRecord> EditAndApproveAsync(
        string runId, string approverId, string editedDraftJson, string? reason, CancellationToken ct = default)
    {
        var run = ParseRunId(runId);
        await ThrowIfAlreadyDecidedAsync(run, ct);

        if (string.IsNullOrWhiteSpace(editedDraftJson))
        {
            throw new InvalidEditedContentException("edited content must not be empty");
        }

        DraftJsonShapeValidator.Validate(editedDraftJson);

        var record = new ApprovalRecord(
            RunId: run,
            Decision: ApprovalDecision.EditedAndApproved,
            DecidedAtUtc: DateTimeOffset.UtcNow,
            ApprovedBy: approverId,
            Notes: reason,
            ModifiedDraftJson: editedDraftJson);

        await _repository.AddAsync(record, ct);
        return record;
    }

    public Task<ApprovalRecord?> GetForRunAsync(string runId, CancellationToken ct = default)
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