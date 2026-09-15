using CitizenServicesCopilot.Application.Common.Interfaces.Ingestion;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services.Ingestion;

/// <summary>
/// Handles extraction of plain text document content from string or stream sources.
/// </summary>
public class PlainTextExtractor : IDocumentExtractor
{
    public bool CanExtract(DocumentSourceInput input)
    {
        if (input == null) return false;
        return !string.IsNullOrWhiteSpace(input.RawText) || input.StreamContent != null;
    }

    public async Task<ExtractedDocument> ExtractAsync(DocumentSourceInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        string content;
        if (!string.IsNullOrEmpty(input.RawText))
        {
            content = input.RawText;
        }
        else if (input.StreamContent != null)
        {
            using var reader = new StreamReader(input.StreamContent, leaveOpen: true);
            content = await reader.ReadToEndAsync(ct);
        }
        else
        {
            content = string.Empty;
        }

        return new ExtractedDocument(
            Title: input.Title,
            Source: input.Source,
            Version: input.Version,
            Category: input.Category,
            Content: content.Trim()
        );
    }
}
