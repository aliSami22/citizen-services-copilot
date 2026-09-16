using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Interfaces.Retrieval;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Retrieval;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Infrastructure.Retrieval;

/// <summary>
/// Infrastructure adapter for grounded hybrid retrieval combining pgvector dense cosine search,
/// keyword lexical matching, RRF fusion, grounded refusal thresholding, and citation binding.
/// </summary>
public class GroundedRetriever : IRetrievalService
{
    private readonly AppDbContext _context;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IQueryEnhancer _queryEnhancer;
    private readonly HybridFusionEngine _fusionEngine;
    private readonly ILogger<GroundedRetriever> _logger;

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "in", "on", "at", "to", "for", "of", "with", "is", "are", "was",
        "what", "how", "where", "can", "i", "you", "my", "do", "does", "and", "or", "it",
        "هل", "ما", "ماذا", "كيف", "اين", "في", "من", "على", "إلى", "عن", "مع", "هو", "هي"
    };

    public GroundedRetriever(
        AppDbContext context,
        IEmbeddingGenerator embeddingGenerator,
        IQueryEnhancer queryEnhancer,
        HybridFusionEngine fusionEngine,
        ILogger<GroundedRetriever> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _queryEnhancer = queryEnhancer ?? throw new ArgumentNullException(nameof(queryEnhancer));
        _fusionEngine = fusionEngine ?? throw new ArgumentNullException(nameof(fusionEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return RetrievalResult.Refuse("Query cannot be empty or whitespace.", 0.0);
        }

        // 1. Query Enhancement (domain colloquial mapping)
        var enhancedQuery = await _queryEnhancer.EnhanceQueryAsync(query.Query, ct);
        _logger.LogInformation("Retriever: Raw query '{Raw}' enhanced to '{Enhanced}'", query.Query, enhancedQuery);

        // 2. Fetch all chunks with document metadata
        var chunks = await _context.DocumentChunks
            .Include(c => c.Document)
            .ToListAsync(ct);

        if (chunks.Count == 0)
        {
            _logger.LogWarning("Retriever: Document corpus is empty. Refusing query.");
            return RetrievalResult.Refuse(RetrievalResult.DefaultRefusalMessage, 0.0);
        }

        // 3. Dense Vector Search
        var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(enhancedQuery, ct);
        var denseRanked = ScoreDense(chunks, queryEmbedding);

        // 4. Keyword Lexical Search
        var keywordRanked = ScoreKeyword(chunks, enhancedQuery);

        // 5. Reciprocal Rank Fusion (RRF)
        var fusedCandidates = _fusionEngine.Fuse(
            denseRanked,
            keywordRanked,
            topK: Math.Max(query.TopK * 2, 8)
        );

        if (fusedCandidates.Count == 0)
        {
            _logger.LogInformation("Retriever: No matching candidates found for query '{Query}'.", query.Query);
            return RetrievalResult.Refuse(RetrievalResult.DefaultRefusalMessage, 0.0);
        }

        double maxScore = fusedCandidates.Max(c => c.CombinedScore);

        // 6. Strict Grounded Refusal Evaluation
        var survivingCandidates = fusedCandidates
            .Where(c => c.CombinedScore >= query.MinRelevanceScore)
            .Take(query.TopK)
            .ToList();

        if (survivingCandidates.Count == 0 || maxScore < query.MinRelevanceScore)
        {
            _logger.LogInformation(
                "Retriever: Grounded refusal triggered. Max score {MaxScore:F4} below threshold {Threshold:F4}.",
                maxScore, query.MinRelevanceScore);

            return RetrievalResult.Refuse(RetrievalResult.DefaultRefusalMessage, maxScore);
        }

        // 7. Structured Citation Binding
        var citations = survivingCandidates.Select(c => new Citation(
            DocumentId: c.Chunk.DocumentId,
            ChunkId: c.Chunk.Id,
            DocumentTitle: c.Chunk.Document?.Title ?? "Official Document",
            Source: c.Chunk.Document?.Source ?? "Government Portal",
            Version: c.Chunk.Document?.Version ?? "1.0",
            Section: c.Chunk.Section,
            PageNumber: c.Chunk.PageNumber,
            Excerpt: ExtractExcerpt(c.Chunk.Content, 200),
            RelevanceScore: c.CombinedScore
        )).ToList();

        _logger.LogInformation(
            "Retriever: Successfully retrieved {Count} chunks (MaxScore: {MaxScore:F4}) above threshold {Threshold:F4}.",
            survivingCandidates.Count, maxScore, query.MinRelevanceScore);

        return RetrievalResult.Success(survivingCandidates, citations, maxScore);
    }

    private static IReadOnlyList<(DocumentChunk Chunk, double Score)> ScoreDense(
        IReadOnlyList<DocumentChunk> chunks,
        float[] queryEmbedding)
    {
        var scored = new List<(DocumentChunk Chunk, double Score)>();

        foreach (var chunk in chunks)
        {
            if (chunk.Embedding != null && chunk.Embedding.Length > 0)
            {
                double cosine = ComputeCosineSimilarity(queryEmbedding, chunk.Embedding);
                if (cosine > 0.0)
                {
                    scored.Add((chunk, Math.Round(cosine, 4)));
                }
            }
        }

        return scored.OrderByDescending(s => s.Score).ToList();
    }

    private static IReadOnlyList<(DocumentChunk Chunk, double Score)> ScoreKeyword(
        IReadOnlyList<DocumentChunk> chunks,
        string query)
    {
        var terms = query.Split(new[] { ' ', '?', '!', '.', ',', ':', ';', '-', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !Stopwords.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (terms.Count == 0)
        {
            return Array.Empty<(DocumentChunk Chunk, double Score)>();
        }

        var scored = new List<(DocumentChunk Chunk, double Score)>();

        foreach (var chunk in chunks)
        {
            double score = CalculateKeywordScore(chunk, terms);
            if (score > 0.0)
            {
                scored.Add((chunk, Math.Round(score, 4)));
            }
        }

        return scored.OrderByDescending(s => s.Score).ToList();
    }

    private static double CalculateKeywordScore(DocumentChunk chunk, List<string> terms)
    {
        int matchedTerms = 0;
        int occurrences = 0;

        string title = chunk.Document?.Title ?? string.Empty;
        string section = chunk.Section ?? string.Empty;
        string content = chunk.Content ?? string.Empty;

        foreach (var term in terms)
        {
            bool inTitle = title.Contains(term, StringComparison.OrdinalIgnoreCase);
            bool inSection = section.Contains(term, StringComparison.OrdinalIgnoreCase);
            bool inContent = content.Contains(term, StringComparison.OrdinalIgnoreCase);

            if (inTitle || inSection || inContent)
            {
                matchedTerms++;
                if (inTitle) occurrences += 3;
                if (inSection) occurrences += 2;
                if (inContent) occurrences += 1;
            }
        }

        if (matchedTerms == 0) return 0.0;

        double termCoverage = (double)matchedTerms / terms.Count;
        double densityBonus = Math.Min(0.3, occurrences * 0.04);

        return Math.Min(1.0, termCoverage * 0.7 + densityBonus);
    }

    private static double ComputeCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0.0;

        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0.0 || normB <= 0.0) return 0.0;

        double similarity = dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Math.Max(0.0, similarity);
    }

    private static string ExtractExcerpt(string content, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var trimmed = content.Trim();
        if (trimmed.Length <= maxChars) return trimmed;
        return trimmed[..maxChars] + "...";
    }
}
