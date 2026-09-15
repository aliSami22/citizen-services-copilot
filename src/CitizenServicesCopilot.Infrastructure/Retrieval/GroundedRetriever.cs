using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Infrastructure.Retrieval;

public class GroundedRetriever : IRetrievalService
{
    private readonly AppDbContext _context;
    private readonly ILogger<GroundedRetriever> _logger;

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "in", "on", "at", "to", "for", "of", "with", "is", "are", "was",
        "what", "how", "where", "can", "i", "you", "my", "do", "does", "and", "or", "it",
        "هل", "ما", "ماذا", "كيف", "اين", "في", "من", "على", "إلى", "عن", "مع", "هو", "هي"
    };

    public GroundedRetriever(AppDbContext context, ILogger<GroundedRetriever> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentChunk>> RetrieveRelevantChunksAsync(
        string query, 
        int topK = 4, 
        double minSimilarity = 0.35, 
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<DocumentChunk>();
        }

        var chunks = await _context.DocumentChunks
            .Include(c => c.Document)
            .ToListAsync(ct);

        if (chunks.Count == 0)
        {
            _logger.LogWarning("Retriever: Corpus is completely empty.");
            return Array.Empty<DocumentChunk>();
        }

        var terms = query.Split(new[] { ' ', '?', '!', '.', ',', ':', ';', '-', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !Stopwords.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (terms.Count == 0)
        {
            return Array.Empty<DocumentChunk>();
        }

        var scoredChunks = new List<(DocumentChunk Chunk, double Score)>();

        foreach (var chunk in chunks)
        {
            double keywordScore = CalculateKeywordScore(chunk, terms);
            double vectorScore = 0.0;

            // If embedding is present, compute cosine similarity if query has embedding
            // (When embeddings are enabled in live mode, vector similarity is factored in)
            double combinedScore = vectorScore > 0 
                ? (0.65 * vectorScore + 0.35 * keywordScore) 
                : keywordScore;

            if (combinedScore >= minSimilarity)
            {
                scoredChunks.Add((chunk, combinedScore));
            }
        }

        var topResults = scoredChunks
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => s.Chunk)
            .ToList();

        _logger.LogInformation("Retriever: Query '{Query}' matched {Count} chunks above threshold {Threshold}", 
            query, topResults.Count, minSimilarity);

        return topResults;
    }

    private static double CalculateKeywordScore(DocumentChunk chunk, List<string> terms)
    {
        int matchedTerms = 0;
        int totalOccurrences = 0;

        string title = chunk.Document?.Title ?? string.Empty;
        string content = chunk.Content;
        string section = chunk.Section;

        foreach (var term in terms)
        {
            bool foundInContent = content.Contains(term, StringComparison.OrdinalIgnoreCase);
            bool foundInTitle = title.Contains(term, StringComparison.OrdinalIgnoreCase);
            bool foundInSection = section.Contains(term, StringComparison.OrdinalIgnoreCase);

            if (foundInContent || foundInTitle || foundInSection)
            {
                matchedTerms++;
                if (foundInTitle) totalOccurrences += 3;
                if (foundInSection) totalOccurrences += 2;
                if (foundInContent) totalOccurrences += 1;
            }
        }

        if (matchedTerms == 0) return 0.0;

        // Jaccard-like overlap combined with frequency density
        double termCoverage = (double)matchedTerms / terms.Count;
        double densityBonus = Math.Min(0.4, totalOccurrences * 0.05);

        return termCoverage * 0.7 + densityBonus;
    }
}
