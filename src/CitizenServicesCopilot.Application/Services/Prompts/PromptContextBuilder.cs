using System.Text;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Services.Prompts;

/// <summary>
/// Renders a consistent, citable excerpt block from retrieved chunks for
/// injection into agent prompt templates.
/// </summary>
internal static class PromptContextBuilder
{
    public static string FormatChunks(IReadOnlyList<DocumentChunk> chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            sb.AppendLine($"[ID: {chunk.Id} | Title: {chunk.Document?.Title ?? "Official Decree"} | Source: {chunk.Document?.Source ?? "Government Gazette"} | Page: {chunk.PageNumber} | Section: {chunk.Section}]");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}