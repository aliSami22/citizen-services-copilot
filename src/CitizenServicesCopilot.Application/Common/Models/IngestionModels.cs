namespace CitizenServicesCopilot.Application.Common.Models;

/// <summary>
/// Source input data provided for document extraction.
/// </summary>
public record DocumentSourceInput(
    string Title,
    string Source,
    string Version = "1.0",
    string Category = "General",
    Stream? StreamContent = null,
    string? RawText = null
);

/// <summary>
/// Intermediate representation of an extracted document before chunking.
/// Decouples format-specific extractors (raw text, PDF, HTML) from chunking strategies.
/// </summary>
public record ExtractedDocument(
    string Title,
    string Source,
    string Version,
    string Category,
    string Content,
    IReadOnlyList<ExtractedSection>? Sections = null
);

/// <summary>
/// Section information within an extracted document, if available from structured sources.
/// </summary>
public record ExtractedSection(
    string Title,
    string Content,
    int PageNumber = 1
);
