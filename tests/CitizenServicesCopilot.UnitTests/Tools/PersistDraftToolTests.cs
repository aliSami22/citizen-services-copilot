using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Services.Tools;
using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.UnitTests.Tools;

public class PersistDraftToolTests
{
    [Fact]
    public async Task NoApprovalRecord_ThrowsPersistNotApproved_AndDoesNotWrite()
    {
        var drafts = new FakeDrafts();
        var tool = new PersistDraftTool(new FakeApprovalService(null), drafts);
        var runId = Guid.NewGuid();

        await Assert.ThrowsAsync<PersistNotApprovedException>(
            () => tool.ExecuteAsync(Args(runId, "draft-json"), CancellationToken.None));

        Assert.Empty(drafts.Written);
    }

    [Fact]
    public async Task PendingApprovalRecord_ThrowsPersistNotApproved()
    {
        var drafts = new FakeDrafts();
        var approval = new ApprovalRecord(Guid.NewGuid(), ApprovalDecision.Pending, DateTimeOffset.UtcNow, "approver", null, null);
        var tool = new PersistDraftTool(new FakeApprovalService(approval), drafts);
        var runId = Guid.NewGuid();

        await Assert.ThrowsAsync<PersistNotApprovedException>(
            () => tool.ExecuteAsync(Args(runId, "draft-json"), CancellationToken.None));

        Assert.Empty(drafts.Written);
    }

    [Fact]
    public async Task RejectedApprovalRecord_ThrowsPersistNotApproved()
    {
        var drafts = new FakeDrafts();
        var approval = new ApprovalRecord(Guid.NewGuid(), ApprovalDecision.Rejected, DateTimeOffset.UtcNow, "approver", "denied", null);
        var tool = new PersistDraftTool(new FakeApprovalService(approval), drafts);
        var runId = Guid.NewGuid();

        await Assert.ThrowsAsync<PersistNotApprovedException>(
            () => tool.ExecuteAsync(Args(runId, "draft-json"), CancellationToken.None));

        Assert.Empty(drafts.Written);
    }

    [Fact]
    public async Task ApprovedRecord_WritesPayloadAndReturnsSuccess()
    {
        var drafts = new FakeDrafts();
        var approval = new ApprovalRecord(Guid.NewGuid(), ApprovalDecision.Approved, DateTimeOffset.UtcNow, "approver", null, null);
        var tool = new PersistDraftTool(new FakeApprovalService(approval), drafts);
        var runId = Guid.NewGuid();

        var result = await tool.ExecuteAsync(Args(runId, "approved-draft"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Payload.GetProperty("fromEditedDraft").GetBoolean());
        Assert.Single(drafts.Written);
        Assert.Equal(runId, drafts.Written[0].RunId);
        Assert.Equal("approved-draft", drafts.Written[0].DraftJson);
    }

    [Fact]
    public async Task EditedAndApproved_WritesEditedContent()
    {
        var drafts = new FakeDrafts();
        var approval = new ApprovalRecord(
            Guid.NewGuid(), ApprovalDecision.EditedAndApproved, DateTimeOffset.UtcNow, "approver", "edited by reviewer", "edited-draft");
        var tool = new PersistDraftTool(new FakeApprovalService(approval), drafts);
        var runId = Guid.NewGuid();

        var result = await tool.ExecuteAsync(Args(runId, "original-draft"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Payload.GetProperty("fromEditedDraft").GetBoolean());
        Assert.Equal("edited-draft", drafts.Written[0].DraftJson);
    }

    [Fact]
    public async Task EditedAndApproved_WithoutEditedContent_WritesPayloadDraft()
    {
        var drafts = new FakeDrafts();
        var approval = new ApprovalRecord(
            Guid.NewGuid(), ApprovalDecision.EditedAndApproved, DateTimeOffset.UtcNow, "approver", null, null);
        var tool = new PersistDraftTool(new FakeApprovalService(approval), drafts);
        var runId = Guid.NewGuid();

        var result = await tool.ExecuteAsync(Args(runId, "payload-draft"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("payload-draft", drafts.Written[0].DraftJson);
    }

    [Fact]
    public async Task MissingRunId_ReturnsFailedWithoutWrite()
    {
        var drafts = new FakeDrafts();
        var approval = new ApprovalRecord(Guid.NewGuid(), ApprovalDecision.Approved, DateTimeOffset.UtcNow, "approver", null, null);
        var tool = new PersistDraftTool(new FakeApprovalService(approval), drafts);

        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { draftJson = "draft" }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(drafts.Written);
    }

    [Fact]
    public async Task MissingDraftJson_ReturnsFailedWithoutWrite()
    {
        var drafts = new FakeDrafts();
        var approval = new ApprovalRecord(Guid.NewGuid(), ApprovalDecision.Approved, DateTimeOffset.UtcNow, "approver", null, null);
        var tool = new PersistDraftTool(new FakeApprovalService(approval), drafts);

        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { runId = Guid.NewGuid().ToString() }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(drafts.Written);
    }

    private static JsonElement Args(Guid runId, string draftJson)
        => JsonSerializer.SerializeToElement(new { runId = runId.ToString(), draftJson });

    private sealed class FakeApprovalService : IApprovalService
    {
        private readonly ApprovalRecord? _record;

        public FakeApprovalService(ApprovalRecord? record) => _record = record;

        public Task<ApprovalRecord> ApproveAsync(string runId, string approverId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ApprovalRecord> RejectAsync(string runId, string approverId, string reason, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ApprovalRecord> EditAndApproveAsync(
            string runId, string approverId, string editedDraftJson, string? reason, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ApprovalRecord?> GetForRunAsync(string runId, CancellationToken ct = default)
            => Task.FromResult(_record);
    }

    private sealed class FakeDrafts : IPersistedDraftRepository
    {
        public List<(Guid RunId, string DraftJson)> Written { get; } = new();

        public Task PersistAsync(Guid runId, string draftJson, CancellationToken ct = default)
        {
            Written.Add((runId, draftJson));
            return Task.CompletedTask;
        }
    }
}