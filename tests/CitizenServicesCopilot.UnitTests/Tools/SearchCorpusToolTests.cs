using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Tools;
using CitizenServicesCopilot.Domain.Entities;
using Citation = CitizenServicesCopilot.Application.Common.Models.Citation;

namespace CitizenServicesCopilot.UnitTests.Tools;

public class SearchCorpusToolTests
{
    [Fact]
    public async Task HappyPath_ReturnsChunksAndCitations()
    {
        var retrieval = new FakeRetrieval(RetrievalResult.Success(
            new[]
            {
                new ScoredChunk(Chunk("eligibility text"), 0.7, 0.3, 0.55),
                new ScoredChunk(Chunk("procedure text"), 0.6, 0.2, 0.5)
            },
            new[]
            {
                new Citation(Guid.NewGuid(), Guid.NewGuid(), "Decree 21", "Official Gazette", "3.1", "S1", 2, "excerpt", 0.55)
            },
            0.55));
        var tool = new SearchCorpusTool(retrieval);

        var args = JsonSerializer.SerializeToElement(new { query = "passport renewal" });
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Payload.GetProperty("refused").GetBoolean());
        Assert.Equal(2, result.Payload.GetProperty("chunks").GetArrayLength());
        Assert.Equal(1, result.Payload.GetProperty("citations").GetArrayLength());
        Assert.Equal(4, retrieval.LastQuery!.TopK);
    }

    [Fact]
    public async Task TopK_Override_IsForwarded()
    {
        var retrieval = new FakeRetrieval(RetrievalResult.Success(Array.Empty<ScoredChunk>(), Array.Empty<Citation>(), 0));
        var tool = new SearchCorpusTool(retrieval);

        var args = JsonSerializer.SerializeToElement(new { query = "resident card", topK = 10 });
        await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Equal(10, retrieval.LastQuery!.TopK);
    }

    [Fact]
    public async Task RefusalResult_SurfacesRefusedFlagAndReason()
    {
        var retrieval = new FakeRetrieval(RetrievalResult.Refuse("no evidence"));
        var tool = new SearchCorpusTool(retrieval);
        var args = JsonSerializer.SerializeToElement(new { query = "unicorn law" });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Payload.GetProperty("refused").GetBoolean());
        Assert.Equal("no evidence", result.Payload.GetProperty("refusalReason").GetString());
    }

    [Fact]
    public async Task MissingQuery_Fails()
    {
        var tool = new SearchCorpusTool(new FakeRetrieval(null));
        var args = JsonSerializer.SerializeToElement(new { topK = 4 });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("query", result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task InvalidTopK_Fails(int topK)
    {
        var tool = new SearchCorpusTool(new FakeRetrieval(null));
        var args = JsonSerializer.SerializeToElement(new { query = "passport", topK });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("topK", result.Error);
    }

    private static DocumentChunk Chunk(string content)
        => new()
        {
            Content = content,
            PageNumber = 1,
            Section = "S1",
            Document = new Document { Title = "Decree 21", Source = "Official Gazette" }
        };

    private sealed class FakeRetrieval : IRetrievalService
    {
        private readonly RetrievalResult? _result;

        public RetrievalQuery? LastQuery { get; private set; }

        public FakeRetrieval(RetrievalResult? result) => _result = result;

        public Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(_result!);
        }
    }
}