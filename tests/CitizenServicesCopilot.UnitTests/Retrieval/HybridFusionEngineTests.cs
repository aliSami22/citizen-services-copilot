using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Retrieval;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.UnitTests.Retrieval;

public class HybridFusionEngineTests
{
    private static readonly HybridFusionEngine Engine = new();

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DocumentChunk MakeChunk(string content = "content") =>
        new() { Id = Guid.NewGuid(), Content = content };

    private static IReadOnlyList<(DocumentChunk Chunk, double Score)> Ranked(
        params (DocumentChunk Chunk, double Score)[] items) => items;

    // ─── Empty / edge-case tests ──────────────────────────────────────────────

    [Fact]
    public void Fuse_BothListsEmpty_ReturnsEmpty()
    {
        var result = Engine.Fuse(
            Array.Empty<(DocumentChunk, double)>(),
            Array.Empty<(DocumentChunk, double)>());

        Assert.Empty(result);
    }

    [Fact]
    public void Fuse_OnlyDenseList_ReturnsChunksRankedByRrf()
    {
        var c1 = MakeChunk("chunk-1");
        var c2 = MakeChunk("chunk-2");

        var result = Engine.Fuse(
            denseRanked: Ranked((c1, 0.95), (c2, 0.80)),
            keywordRanked: Array.Empty<(DocumentChunk, double)>());

        Assert.Equal(2, result.Count);
        // c1 appeared at rank 1 in dense -> should have higher combined score than c2 at rank 2
        Assert.Equal(c1.Id, result[0].Chunk.Id);
        Assert.Equal(c2.Id, result[1].Chunk.Id);
        Assert.True(result[0].CombinedScore > result[1].CombinedScore);
    }

    [Fact]
    public void Fuse_OnlyKeywordList_ReturnsChunksRankedByRrf()
    {
        var c1 = MakeChunk("chunk-1");
        var c2 = MakeChunk("chunk-2");

        var result = Engine.Fuse(
            denseRanked: Array.Empty<(DocumentChunk, double)>(),
            keywordRanked: Ranked((c1, 0.90), (c2, 0.60)));

        Assert.Equal(2, result.Count);
        Assert.Equal(c1.Id, result[0].Chunk.Id);
    }

    // ─── RRF ranking correctness ───────────────────────────────────────────────

    [Fact]
    public void Fuse_ChunkAtRank1InBothLists_ScoresHighestAndNormalizesTo1()
    {
        var topChunk = MakeChunk("top");
        var otherChunk = MakeChunk("other");

        var result = Engine.Fuse(
            denseRanked: Ranked((topChunk, 1.0), (otherChunk, 0.5)),
            keywordRanked: Ranked((topChunk, 1.0), (otherChunk, 0.4)),
            topK: 5);

        Assert.Equal(topChunk.Id, result[0].Chunk.Id);
        // Rank 1 in both lists -> raw RRF = 1/(60+1) + 1/(60+1) = 2/61
        // normalised by same max -> 1.0
        Assert.Equal(1.0, result[0].CombinedScore);
    }

    [Fact]
    public void Fuse_ChunkInSingleListOnly_ScoresExactlyHalfOfDualRankChunk()
    {
        var denseChunk = MakeChunk("dense-only");
        var keywordChunk = MakeChunk("keyword-only");

        // denseChunk: rank 1 in dense ONLY     -> raw RRF = 1/61; normalized = 1/61 / (2/61) = 0.5
        // keywordChunk: rank 1 in keyword ONLY -> raw RRF = 1/61; normalized = 0.5
        // If they were rank 1 in BOTH:         -> raw RRF = 2/61; normalized = 1.0
        var result = Engine.Fuse(
            denseRanked: Ranked((denseChunk, 1.0)),
            keywordRanked: Ranked((keywordChunk, 1.0)),
            topK: 5);

        var denseResult = result.First(r => r.Chunk.Id == denseChunk.Id);
        var keywordResult = result.First(r => r.Chunk.Id == keywordChunk.Id);

        Assert.Equal(0.5, denseResult.CombinedScore);
        Assert.Equal(0.5, keywordResult.CombinedScore);
    }

    [Fact]
    public void Fuse_RankOrderMatters_LowerRankYieldsLowerScore()
    {
        var rank1 = MakeChunk("rank1");
        var rank2 = MakeChunk("rank2");
        var rank3 = MakeChunk("rank3");

        var result = Engine.Fuse(
            denseRanked: Ranked((rank1, 0.95), (rank2, 0.85), (rank3, 0.70)),
            keywordRanked: Array.Empty<(DocumentChunk, double)>(),
            topK: 10);

        Assert.Equal(rank1.Id, result[0].Chunk.Id);
        Assert.Equal(rank2.Id, result[1].Chunk.Id);
        Assert.Equal(rank3.Id, result[2].Chunk.Id);
        Assert.True(result[0].CombinedScore > result[1].CombinedScore);
        Assert.True(result[1].CombinedScore > result[2].CombinedScore);
    }

    [Fact]
    public void Fuse_NormalizedScores_AreAlwaysBetween0And1()
    {
        var chunks = Enumerable.Range(0, 10).Select(_ => MakeChunk()).ToList();

        var dense = chunks.Select((c, i) => (c, Score: 1.0 - i * 0.05)).ToArray();
        var keyword = chunks.OrderBy(c => c.Id).Select((c, i) => (c, Score: 1.0 - i * 0.05)).ToArray();

        var result = Engine.Fuse(dense, keyword, topK: 20);

        Assert.All(result, r =>
        {
            Assert.True(r.CombinedScore >= 0.0 && r.CombinedScore <= 1.0,
                $"Score {r.CombinedScore} out of [0,1] range");
        });
    }

    // ─── topK enforcement ─────────────────────────────────────────────────────

    [Fact]
    public void Fuse_TopKLimitsOutput()
    {
        var chunks = Enumerable.Range(0, 20).Select(_ => MakeChunk()).ToList();
        var dense = chunks.Select((c, i) => (c, Score: 1.0 - i * 0.04)).ToArray();

        var result = Engine.Fuse(dense, Array.Empty<(DocumentChunk, double)>(), topK: 5);

        Assert.Equal(5, result.Count);
    }

    // ─── Score binding ────────────────────────────────────────────────────────

    [Fact]
    public void Fuse_ScoredChunkBindsOriginalDenseAndKeywordScores()
    {
        var chunk = MakeChunk("binding-test");
        const double denseScore = 0.88;
        const double keywordScore = 0.55;

        var result = Engine.Fuse(
            denseRanked: Ranked((chunk, denseScore)),
            keywordRanked: Ranked((chunk, keywordScore)),
            topK: 5);

        Assert.Single(result);
        Assert.Equal(denseScore, result[0].DenseScore);
        Assert.Equal(keywordScore, result[0].KeywordScore);
    }

    [Fact]
    public void Fuse_ChunkAppearsOnlyInKeyword_DenseScoreIsZero()
    {
        var chunk = MakeChunk("keyword-only");

        var result = Engine.Fuse(
            denseRanked: Array.Empty<(DocumentChunk, double)>(),
            keywordRanked: Ranked((chunk, 0.75)),
            topK: 5);

        Assert.Single(result);
        Assert.Equal(0.0, result[0].DenseScore);
        Assert.Equal(0.75, result[0].KeywordScore);
    }

    // ─── Determinism ─────────────────────────────────────────────────────────

    [Fact]
    public void Fuse_IsIdempotent_SameInputSameOutput()
    {
        var c1 = MakeChunk("a");
        var c2 = MakeChunk("b");
        var c3 = MakeChunk("c");

        var dense = Ranked((c1, 0.9), (c2, 0.8), (c3, 0.7));
        var keyword = Ranked((c2, 0.85), (c1, 0.75), (c3, 0.60));

        var run1 = Engine.Fuse(dense, keyword, topK: 10);
        var run2 = Engine.Fuse(dense, keyword, topK: 10);

        Assert.Equal(run1.Count, run2.Count);
        for (int i = 0; i < run1.Count; i++)
        {
            Assert.Equal(run1[i].Chunk.Id, run2[i].Chunk.Id);
            Assert.Equal(run1[i].CombinedScore, run2[i].CombinedScore);
        }
    }
}
