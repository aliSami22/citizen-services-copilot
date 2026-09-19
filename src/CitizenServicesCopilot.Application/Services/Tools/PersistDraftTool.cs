using System.Text.Json;
using CitizenServicesCopilot.Application.Common;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// WRITE tool that persists an approved draft. It re-checks the approval
/// record as a second line of defence and refuses to write when no Approved
/// or EditedAndApproved decision exists for the run (§3 R-5).
/// </summary>
public sealed class PersistDraftTool : ITool
{
    private readonly IApprovalRecordRepository _approvals;
    private readonly IPersistedDraftRepository _drafts;

    public string Name => ToolCatalog.PersistDraft;

    public bool IsWrite => true;

    public static ToolSchema Schema { get; } = new(
        ToolName: ToolCatalog.PersistDraft,
        Parameters: new[]
        {
            new ToolParameterSpec("runId", JsonValueKind.String, IsRequired: true),
            new ToolParameterSpec("draftJson", JsonValueKind.String, IsRequired: true)
        });

    public PersistDraftTool(IApprovalRecordRepository approvals, IPersistedDraftRepository drafts)
    {
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
    }

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("runId", out var runIdProp) ||
            runIdProp.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(runIdProp.GetString(), out var runId))
        {
            return ToolResult.Failed("missing or invalid required field 'runId'.");
        }

        if (!args.TryGetProperty("draftJson", out var draftJsonProp) ||
            draftJsonProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(draftJsonProp.GetString()))
        {
            return ToolResult.Failed("missing or invalid required field 'draftJson'.");
        }

        var draftJson = draftJsonProp.GetString()!;

        var approval = await _approvals.GetForRunAsync(runId, ct);
        if (approval is null || approval.Decision is not (ApprovalDecision.Approved or ApprovalDecision.EditedAndApproved))
        {
            throw new PersistNotApprovedException(runId);
        }

        // §3 R-5: edit-and-approve persists the edited content.
        if (approval.Decision == ApprovalDecision.EditedAndApproved &&
            !string.IsNullOrWhiteSpace(approval.ModifiedDraftJson))
        {
            draftJson = approval.ModifiedDraftJson;
        }

        await _drafts.PersistAsync(runId, draftJson, ct);

        var payload = JsonSerializer.SerializeToElement(new
        {
            runId = runId,
            persisted = true,
            fromEditedDraft = approval.Decision == ApprovalDecision.EditedAndApproved
        }, JsonOptions.CamelCase);

        return ToolResult.Succeeded(payload);
    }
}