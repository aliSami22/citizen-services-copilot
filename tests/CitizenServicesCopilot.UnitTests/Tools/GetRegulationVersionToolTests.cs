using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Services.Tools;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.UnitTests.Tools;

public class GetRegulationVersionToolTests
{
    private static readonly DateTime IssuedAt = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExistingDocument_ReturnsVersionAndIssueDate()
    {
        var documentId = Guid.NewGuid();
        var tool = new GetRegulationVersionTool(new FakeDocumentRepo(new Document
        {
            Id = documentId,
            Title = "Decree on Passports",
            Version = "3.2",
            Source = "Official Gazette",
            CreatedAtUtc = IssuedAt
        }));
        var args = JsonSerializer.SerializeToElement(new { documentId = documentId.ToString() });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("3.2", result.Payload.GetProperty("version").GetString());
        Assert.Equal(IssuedAt, result.Payload.GetProperty("issuedAt").GetDateTime());
        Assert.Equal(documentId, result.Payload.GetProperty("documentId").GetGuid());
    }

    [Fact]
    public async Task UnknownDocument_Fails()
    {
        var tool = new GetRegulationVersionTool(new FakeDocumentRepo(null));
        var args = JsonSerializer.SerializeToElement(new { documentId = Guid.NewGuid().ToString() });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task InvalidDocumentId_Fails()
    {
        var tool = new GetRegulationVersionTool(new FakeDocumentRepo(null));
        var args = JsonSerializer.SerializeToElement(new { documentId = "not-a-guid" });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("GUID", result.Error);
    }

    [Fact]
    public async Task MissingDocumentId_Fails()
    {
        var tool = new GetRegulationVersionTool(new FakeDocumentRepo(null));
        var args = JsonSerializer.SerializeToElement(new { });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("documentId", result.Error);
    }

    private sealed class FakeDocumentRepo : IDocumentRepository
    {
        private readonly Document? _document;

        public FakeDocumentRepo(Document? document) => _document = document;

        public Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_document);

        public Task<Document?> GetByContentHashAsync(string contentHash, CancellationToken ct = default)
            => Task.FromResult(_document);

        public Task AddAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;
    }
}