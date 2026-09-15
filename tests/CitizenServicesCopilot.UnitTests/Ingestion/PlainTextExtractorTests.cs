using System.Text;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Ingestion;

namespace CitizenServicesCopilot.UnitTests.Ingestion;

public class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _extractor = new();

    [Fact]
    public void CanExtract_WithRawText_ReturnsTrue()
    {
        var input = new DocumentSourceInput(
            Title: "Test",
            Source: "test.txt",
            RawText: "Hello world"
        );

        var result = _extractor.CanExtract(input);

        Assert.True(result);
    }

    [Fact]
    public void CanExtract_WithStreamContent_ReturnsTrue()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Stream content"));
        var input = new DocumentSourceInput(
            Title: "Stream Doc",
            Source: "stream.txt",
            StreamContent: stream
        );

        var result = _extractor.CanExtract(input);

        Assert.True(result);
    }

    [Fact]
    public void CanExtract_WithNullInput_ReturnsFalse()
    {
        var result = _extractor.CanExtract(null!);

        Assert.False(result);
    }

    [Fact]
    public async Task ExtractAsync_WithRawText_ExtractsMetadataAndTrimsContent()
    {
        var input = new DocumentSourceInput(
            Title: "Citizen Guide",
            Source: "gov-portal/guide-1",
            Version: "2.1",
            Category: "Social Security",
            RawText: "   Important citizen rights and procedures.   "
        );

        var result = await _extractor.ExtractAsync(input);

        Assert.Equal("Citizen Guide", result.Title);
        Assert.Equal("gov-portal/guide-1", result.Source);
        Assert.Equal("2.1", result.Version);
        Assert.Equal("Social Security", result.Category);
        Assert.Equal("Important citizen rights and procedures.", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_WithStreamContent_ReadsEntireStream()
    {
        var text = "Data read from stream correctly.";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var input = new DocumentSourceInput(
            Title: "From Stream",
            Source: "file.txt",
            StreamContent: stream
        );

        var result = await _extractor.ExtractAsync(input);

        Assert.Equal(text, result.Content);
        Assert.Equal("From Stream", result.Title);
    }

    [Fact]
    public async Task ExtractAsync_WithNullInput_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _extractor.ExtractAsync(null!));
    }
}
