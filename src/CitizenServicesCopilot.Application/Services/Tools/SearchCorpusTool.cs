using System.Text.Json;
using CitizenServicesCopilot.Application.Common;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// Read tool that performs grounded corpus retrieval via the retrieval port
/// and returns the resulting chunks and citations.
/// </summary>
public sealed class SearchCorpusTool : ITool
{
    private readonly IRetrievalService _retrievalService;

    public string Name => ToolCatalog.SearchCorpus;

    public bool IsWrite => false;

    public static ToolSchema Schema { get; } = new(
        ToolName: ToolCatalog.SearchCorpus,
        Parameters: new[]
        {
            new ToolParameterSpec("query", JsonValueKind.String, IsRequired: true),
            new ToolParameterSpec("topK", JsonValueKind.Number, IsRequired: false)
        });

    public SearchCorpusTool(IRetrievalService retrievalService)
    {
        _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
    }

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("query", out var queryProp) ||
            queryProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(queryProp.GetString()))
        {
            return ToolResult.Failed("missing or invalid required field 'query'.");
        }

        var query = queryProp.GetString()!.Trim();

        var topK = 4;
        if (args.TryGetProperty("topK", out var topKProp) && topKProp.ValueKind != JsonValueKind.Null)
        {
            if (topKProp.ValueKind != JsonValueKind.Number)
            {
                return ToolResult.Failed("field 'topK' must be a number.");
            }

            int parsed;
            try
            {
                parsed = topKProp.GetInt32();
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
            {
                return ToolResult.Failed("field 'topK' is out of range.");
            }

            if (parsed < 1 || parsed > 50)
            {
                return ToolResult.Failed("field 'topK' must be between 1 and 50.");
            }

            topK = parsed;
        }

        var result = await _retrievalService.RetrieveAsync(new RetrievalQuery(query, topK), ct);

        var payload = JsonSerializer.SerializeToElement(new
        {
            refused = result.IsRefusal,
            refusalReason = result.RefusalReason,
            maxScore = result.MaxScore,
            chunks = result.Chunks.Select(c => new
            {
                chunkId = c.Chunk.Id,
                documentId = c.Chunk.DocumentId,
                title = c.Chunk.Document?.Title,
                source = c.Chunk.Document?.Source,
                pageNumber = c.Chunk.PageNumber,
                section = c.Chunk.Section,
                content = c.Chunk.Content,
                denseScore = c.DenseScore,
                keywordScore = c.KeywordScore,
                combinedScore = c.CombinedScore
            }),
            citations = result.Citations.Select(ci => new
            {
                documentId = ci.DocumentId,
                chunkId = ci.ChunkId,
                documentTitle = ci.DocumentTitle,
                source = ci.Source,
                version = ci.Version,
                section = ci.Section,
                pageNumber = ci.PageNumber,
                excerpt = ci.Excerpt,
                relevanceScore = ci.RelevanceScore
            })
        }, JsonOptions.CamelCase);

        return ToolResult.Succeeded(payload);
    }
}