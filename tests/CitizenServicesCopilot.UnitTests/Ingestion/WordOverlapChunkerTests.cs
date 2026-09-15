using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Ingestion;

namespace CitizenServicesCopilot.UnitTests.Ingestion;

public class WordOverlapChunkerTests
{
    private readonly WordOverlapChunker _chunker = new(chunkSizeInWords: 500, overlapWords: 50);

    private static ExtractedDocument CreateDoc(string content, string category = "Civil Affairs")
    {
        return new ExtractedDocument(
            Title: "Test Doc",
            Source: "test-source",
            Version: "1.0",
            Category: category,
            Content: content
        );
    }

    private static string GenerateWords(int count)
    {
        return string.Join(" ", Enumerable.Range(1, count).Select(i => $"word{i}"));
    }

    [Fact]
    public void Constructor_InvalidArguments_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordOverlapChunker(chunkSizeInWords: 0, overlapWords: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordOverlapChunker(chunkSizeInWords: -1, overlapWords: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordOverlapChunker(chunkSizeInWords: 100, overlapWords: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordOverlapChunker(chunkSizeInWords: 100, overlapWords: 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordOverlapChunker(chunkSizeInWords: 100, overlapWords: 150));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Chunk_EmptyOrWhitespaceInput_ReturnsZeroChunks(string text)
    {
        var docId = Guid.NewGuid();
        var doc = CreateDoc(text);

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_SingleWord_ReturnsOneChunk()
    {
        var docId = Guid.NewGuid();
        var doc = CreateDoc("SingleWord");

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Single(chunks);
        Assert.Equal("SingleWord", chunks[0].Content);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(docId, chunks[0].DocumentId);
        Assert.Equal(1, chunks[0].PageNumber);
        Assert.Equal("Civil Affairs", chunks[0].Section);
        Assert.Null(chunks[0].Embedding);
    }

    [Fact]
    public void Chunk_499Words_ReturnsOneChunk()
    {
        var docId = Guid.NewGuid();
        var doc = CreateDoc(GenerateWords(499));

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        var wordsInChunk = chunks[0].Content.Split(' ');
        Assert.Equal(499, wordsInChunk.Length);
        Assert.Equal("word1", wordsInChunk.First());
        Assert.Equal("word499", wordsInChunk.Last());
    }

    [Fact]
    public void Chunk_Exactly500Words_ReturnsOneChunk()
    {
        var docId = Guid.NewGuid();
        var doc = CreateDoc(GenerateWords(500));

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        var wordsInChunk = chunks[0].Content.Split(' ');
        Assert.Equal(500, wordsInChunk.Length);
        Assert.Equal("word1", wordsInChunk.First());
        Assert.Equal("word500", wordsInChunk.Last());
    }

    [Fact]
    public void Chunk_501Words_ReturnsTwoChunksWithOverlap()
    {
        var docId = Guid.NewGuid();
        var doc = CreateDoc(GenerateWords(501));

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);

        var chunk0Words = chunks[0].Content.Split(' ');
        var chunk1Words = chunks[1].Content.Split(' ');

        Assert.Equal(500, chunk0Words.Length);
        Assert.Equal("word1", chunk0Words.First());
        Assert.Equal("word500", chunk0Words.Last());

        // Chunk 1 starts at 450 (index 450 = word451) and takes to 501 (51 words)
        Assert.Equal(51, chunk1Words.Length);
        Assert.Equal("word451", chunk1Words.First());
        Assert.Equal("word501", chunk1Words.Last());

        // Overlap verification: 50 overlapping words (word451 through word500)
        var chunk0Tail = chunk0Words.TakeLast(50).ToArray();
        var chunk1Head = chunk1Words.Take(50).ToArray();
        Assert.Equal(chunk0Tail, chunk1Head);
    }

    [Fact]
    public void Chunk_550Words_ReturnsTwoChunksWithExactStep()
    {
        var docId = Guid.NewGuid();
        var doc = CreateDoc(GenerateWords(550));

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);

        var chunk0Words = chunks[0].Content.Split(' ');
        var chunk1Words = chunks[1].Content.Split(' ');

        Assert.Equal(500, chunk0Words.Length);
        Assert.Equal(100, chunk1Words.Length);
        Assert.Equal("word451", chunk1Words.First());
        Assert.Equal("word550", chunk1Words.Last());

        var chunk0Tail = chunk0Words.TakeLast(50).ToArray();
        var chunk1Head = chunk1Words.Take(50).ToArray();
        Assert.Equal(chunk0Tail, chunk1Head);
    }

    [Fact]
    public void Chunk_551Words_ReturnsTwoChunks()
    {
        var docId = Guid.NewGuid();
        var doc = CreateDoc(GenerateWords(551));

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);

        var chunk1Words = chunks[1].Content.Split(' ');
        Assert.Equal(101, chunk1Words.Length);
        Assert.Equal("word451", chunk1Words.First());
        Assert.Equal("word551", chunk1Words.Last());
    }

    [Fact]
    public void Chunk_1000PlusWords_ProducesMultipleSequentialChunksWithContinuousOverlap()
    {
        var docId = Guid.NewGuid();
        // 1200 words:
        // Chunk 0: 0..499 (500 words, word1..word500)
        // Chunk 1: 450..949 (500 words, word451..word950)
        // Chunk 2: 900..1199 (300 words, word901..word1200)
        var doc = CreateDoc(GenerateWords(1200), category: "Passports");

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Equal(3, chunks.Count);

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
            Assert.Equal(docId, chunks[i].DocumentId);
            Assert.Equal(1, chunks[i].PageNumber);
            Assert.Equal("Passports", chunks[i].Section);
            Assert.Null(chunks[i].Embedding);
            Assert.NotEqual(Guid.Empty, chunks[i].Id);
        }

        // Verify continuous overlap across adjacent chunks
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var currentChunkWords = chunks[i].Content.Split(' ');
            var nextChunkWords = chunks[i + 1].Content.Split(' ');

            var overlapFromCurrent = currentChunkWords.TakeLast(50).ToArray();
            var overlapInNext = nextChunkWords.Take(50).ToArray();

            Assert.Equal(overlapFromCurrent, overlapInNext);
        }
    }

    [Fact]
    public void Chunk_PreservesMetadataAcrossAllChunks()
    {
        var docId = Guid.NewGuid();
        var doc = CreateDoc(GenerateWords(950), category: "Taxation");

        var chunks = _chunker.Chunk(docId, doc);

        Assert.Equal(2, chunks.Count);
        foreach (var chunk in chunks)
        {
            Assert.Equal(docId, chunk.DocumentId);
            Assert.Equal("Taxation", chunk.Section);
            Assert.Equal(1, chunk.PageNumber);
            Assert.Null(chunk.Embedding);
        }
    }
}
