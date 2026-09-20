using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Common.Models;

/// <summary>
/// Encapsulates parameters for grounded document chunk retrieval.
/// The absolute MinRelevanceScore gate alone is insufficient: RRF normalization gives a
/// rank-1 candidate in EITHER list a combined score of ~0.50, so the refusal decision also
/// consults per-list floors (raw dense/keyword evidence) and a relative-margin guard.
/// </summary>
public record RetrievalQuery(
    string Query,
    int TopK = 4,
    double MinRelevanceScore = 0.40,
    double KeywordFloor = 0.15,
    double DenseFloor = 0.35,
    double RelativeMarginThreshold = 0.40
);

/// <summary>
/// Represents a retrieved document chunk paired with individual and combined relevance scores.
/// </summary>
public record ScoredChunk(
    DocumentChunk Chunk,
    double DenseScore,
    double KeywordScore,
    double CombinedScore
);

/// <summary>
/// Structured source citation required for evidence grounding and auditability.
/// </summary>
public record Citation(
    Guid DocumentId,
    Guid ChunkId,
    string DocumentTitle,
    string Source,
    string Version,
    string Section,
    int PageNumber,
    string Excerpt,
    double RelevanceScore
);

/// <summary>
/// Result of a hybrid retrieval operation, indicating evidence chunks or refusal status.
/// </summary>
public record RetrievalResult(
    IReadOnlyList<ScoredChunk> Chunks,
    IReadOnlyList<Citation> Citations,
    bool IsRefusal,
    string? RefusalReason,
    double MaxScore
)
{
    public const string DefaultRefusalMessage = "Not enough information in the corpus to ground a reliable answer to this query.";

    public static RetrievalResult Refuse(string? reason = null, double maxScore = 0.0) =>
        new(
            Chunks: Array.Empty<ScoredChunk>(),
            Citations: Array.Empty<Citation>(),
            IsRefusal: true,
            RefusalReason: reason ?? DefaultRefusalMessage,
            MaxScore: maxScore
        );

    public static RetrievalResult Success(IReadOnlyList<ScoredChunk> chunks, IReadOnlyList<Citation> citations, double maxScore) =>
        new(
            Chunks: chunks,
            Citations: citations,
            IsRefusal: false,
            RefusalReason: null,
            MaxScore: maxScore
        );
}
