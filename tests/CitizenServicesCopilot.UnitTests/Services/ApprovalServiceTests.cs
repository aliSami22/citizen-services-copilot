using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Services;
using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.UnitTests.Services;

public class ApprovalServiceTests
{
    private const string ValidDraftJson =
        "{\"isRefusal\":false,\"refusalReason\":null,\"eligibilitySummary\":\"resident of the emirate\"," +
        "\"requiredDocuments\":\"passport\",\"procedureSteps\":\"1. apply\\n2. wait\",\"feesAndTimeline\":\"100 AED\"," +
        "\"citations\":[],\"tokensUsed\":10}";

    [Fact]
    public async Task ApproveAsync_FreshRun_PersistsApprovedRecord()
    {
        var repo = new InMemoryApprovalRepository();
        var service = new ApprovalService(repo);
        var runId = Guid.NewGuid();

        var record = await service.ApproveAsync(runId.ToString(), "officer-1", CancellationToken.None);

        Assert.Equal(ApprovalDecision.Approved, record.Decision);
        Assert.Equal(runId, record.RunId);
        Assert.Equal("officer-1", record.ApprovedBy);
        Assert.Null(record.Notes);
        Assert.Null(record.ModifiedDraftJson);
        Assert.Single(repo.All);
        Assert.Equal(runId, repo.All[0].RunId);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyDecided_ThrowsAndDoesNotOverwrite()
    {
        var runId = Guid.NewGuid();
        var repo = new InMemoryApprovalRepository(
            new ApprovalRecord(runId, ApprovalDecision.Rejected, DateTimeOffset.UtcNow, "officer-1", "denied", null));
        var service = new ApprovalService(repo);

        var ex = await Assert.ThrowsAsync<ApprovalAlreadyDecidedException>(
            () => service.ApproveAsync(runId.ToString(), "officer-2", CancellationToken.None));

        Assert.Equal(runId, ex.RunId);
        Assert.Contains("Rejected", ex.Message);
        Assert.Single(repo.All);
        Assert.Equal(ApprovalDecision.Rejected, repo.All[0].Decision);
    }

    [Fact]
    public async Task ApproveAsync_InvalidRunId_Throws()
    {
        var repo = new InMemoryApprovalRepository();
        var service = new ApprovalService(repo);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ApproveAsync("not-a-guid", "officer-1", CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_RecordsReason()
    {
        var repo = new InMemoryApprovalRepository();
        var service = new ApprovalService(repo);
        var runId = Guid.NewGuid();

        var record = await service.RejectAsync(runId.ToString(), "officer-1", "missing residency evidence", CancellationToken.None);

        Assert.Equal(ApprovalDecision.Rejected, record.Decision);
        Assert.Equal("missing residency evidence", record.Notes);
        Assert.Single(repo.All);
    }

    [Fact]
    public async Task EditAndApproveAsync_ValidJson_PersistsModifiedDraft()
    {
        var repo = new InMemoryApprovalRepository();
        var service = new ApprovalService(repo);
        var runId = Guid.NewGuid();

        var record = await service.EditAndApproveAsync(
            runId.ToString(), "officer-1", ValidDraftJson, "updated fee note", CancellationToken.None);

        Assert.Equal(ApprovalDecision.EditedAndApproved, record.Decision);
        Assert.Equal(ValidDraftJson, record.ModifiedDraftJson);
        Assert.Equal("updated fee note", record.Notes);
        Assert.Single(repo.All);
        Assert.Equal(ValidDraftJson, repo.All[0].ModifiedDraftJson);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"isRefusal\":false}")]
    public async Task EditAndApproveAsync_InvalidJson_ThrowsAndDoesNotPersist(string badJson)
    {
        var repo = new InMemoryApprovalRepository();
        var service = new ApprovalService(repo);
        var runId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidEditedContentException>(
            () => service.EditAndApproveAsync(runId.ToString(), "officer-1", badJson, null, CancellationToken.None));

        Assert.Empty(repo.All);
    }

    [Theory]
    [InlineData("{\"isRefusal\":\"yes\",\"refusalReason\":null,\"eligibilitySummary\":\"x\",\"requiredDocuments\":\"x\",\"procedureSteps\":\"x\",\"feesAndTimeline\":\"x\",\"citations\":[],\"tokensUsed\":1}")]
    [InlineData("{\"isRefusal\":true,\"refusalReason\":null,\"eligibilitySummary\":\"x\",\"requiredDocuments\":\"x\",\"procedureSteps\":\"x\",\"feesAndTimeline\":\"x\",\"citations\":[],\"tokensUsed\":1.5}")]
    public async Task EditAndApproveAsync_WrongFieldTypes_Rejects(string badJson)
    {
        var repo = new InMemoryApprovalRepository();
        var service = new ApprovalService(repo);
        var runId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidEditedContentException>(
            () => service.EditAndApproveAsync(runId.ToString(), "officer-1", badJson, null, CancellationToken.None));

        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task EditAndApproveAsync_EmptyContent_Rejects()
    {
        var repo = new InMemoryApprovalRepository();
        var service = new ApprovalService(repo);
        var runId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidEditedContentException>(
            () => service.EditAndApproveAsync(runId.ToString(), "officer-1", "   ", null, CancellationToken.None));

        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task GetForRunAsync_ReturnsNullOnFreshRun_ThenTheDecision()
    {
        var repo = new InMemoryApprovalRepository();
        var service = new ApprovalService(repo);
        var runId = Guid.NewGuid();

        Assert.Null(await service.GetForRunAsync(runId.ToString(), CancellationToken.None));

        await service.ApproveAsync(runId.ToString(), "officer-1", CancellationToken.None);

        var record = await service.GetForRunAsync(runId.ToString(), CancellationToken.None);
        Assert.NotNull(record);
        Assert.Equal(runId, record!.RunId);
        Assert.Equal(ApprovalDecision.Approved, record.Decision);
    }

    private sealed class InMemoryApprovalRepository : IApprovalRecordRepository
    {
        private readonly Dictionary<Guid, ApprovalRecord> _store = new();

        public IReadOnlyList<ApprovalRecord> All => _store.Values.ToList();

        public InMemoryApprovalRepository()
        {
        }

        public InMemoryApprovalRepository(ApprovalRecord seed) => _store[seed.RunId] = seed;

        public Task<ApprovalRecord?> GetForRunAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(runId, out var record) ? record : null);

        public Task AddAsync(ApprovalRecord record, CancellationToken ct = default)
        {
            _store[record.RunId] = record;
            return Task.CompletedTask;
        }
    }
}