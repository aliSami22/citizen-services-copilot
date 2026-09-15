using System.Text;
using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.ValueObjects;

namespace CitizenServicesCopilot.Application.Agents;

public class ResponseDrafterAgent
{
    private readonly ILLMProvider _llmProvider;

    public const string StandardRefusalPhrase = "Not enough information in the corpus";

    public ResponseDrafterAgent(ILLMProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public async Task<(InquiryDraft Draft, int TokensUsed)> DraftResponseAsync(
        string question,
        string eligibilitySummary,
        string procedureSummary,
        IReadOnlyList<DocumentChunk> contextChunks,
        string modelName,
        CancellationToken ct = default)
    {
        // If no context was retrieved at all, immediate grounded refusal
        if (contextChunks.Count == 0)
        {
            return (new InquiryDraft
            {
                IsRefusal = true,
                RefusalReason = StandardRefusalPhrase,
                EligibilitySummary = StandardRefusalPhrase,
                ProcedureSteps = StandardRefusalPhrase,
                RequiredDocuments = StandardRefusalPhrase,
                FeesAndTimeline = StandardRefusalPhrase,
                Citations = new List<Citation>()
            }, 0);
        }

        var sb = new StringBuilder();
        sb.AppendLine("You are the specialized 'Response Drafter Agent' for Government Citizen Services.");
        sb.AppendLine("Your job is to synthesize findings from our domain agents into a professional response for a citizen.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL MANDATORY RULES:");
        sb.AppendLine($"1. If the provided context DOES NOT contain enough evidence to answer the question, or if the question is unrelated, you MUST reply with exact phrase: '{StandardRefusalPhrase}'. Do NOT attempt to answer using external general knowledge.");
        sb.AppendLine("2. For EVERY claim or requirement, cite the exact source document, page, and section.");
        sb.AppendLine("3. Output your response as a valid JSON object matching this schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"isRefusal\": boolean,");
        sb.AppendLine("  \"refusalReason\": string or null,");
        sb.AppendLine("  \"eligibility\": string,");
        sb.AppendLine("  \"requiredDocuments\": string,");
        sb.AppendLine("  \"procedureSteps\": string,");
        sb.AppendLine("  \"feesAndTimeline\": string");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("--- AVAILABLE EVIDENCE EXCERPTS ---");
        foreach (var chunk in contextChunks)
        {
            sb.AppendLine($"[ID: {chunk.Id} | Title: {chunk.Document?.Title ?? "Official Decree"} | Source: {chunk.Document?.Source ?? "Government Gazette"} | Page: {chunk.PageNumber} | Section: {chunk.Section}]");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }

        sb.AppendLine("--- ELIGIBILITY AGENT FINDINGS ---");
        sb.AppendLine(eligibilitySummary);
        sb.AppendLine();
        sb.AppendLine("--- PROCEDURE AGENT FINDINGS ---");
        sb.AppendLine(procedureSummary);

        var prompt = new LlmPrompt(
            Messages: new List<LlmMessage>
            {
                new("system", sb.ToString()),
                new("user", $"Citizen Question: {question}\nSynthesize the final JSON draft response.")
            },
            ModelName: modelName,
            Temperature: 0.0f,
            MaxTokens: 800
        );

        var response = await _llmProvider.GenerateCompletionAsync(prompt, ct);
        var content = response.Content.Trim();

        // Build citations directly from the grounding chunks
        var citations = contextChunks.Select(c => new Citation(
            DocumentTitle: c.Document?.Title ?? "Official Decree",
            Source: c.Document?.Source ?? "Official Gazette",
            PageNumber: c.PageNumber,
            Section: c.Section,
            QuoteSnippet: c.Content.Length > 160 ? c.Content[..160] + "..." : c.Content
        )).ToList();

        // Check for refusal in raw text
        if (content.Contains(StandardRefusalPhrase, StringComparison.OrdinalIgnoreCase))
        {
            return (new InquiryDraft
            {
                IsRefusal = true,
                RefusalReason = StandardRefusalPhrase,
                EligibilitySummary = StandardRefusalPhrase,
                ProcedureSteps = StandardRefusalPhrase,
                RequiredDocuments = StandardRefusalPhrase,
                FeesAndTimeline = StandardRefusalPhrase,
                Citations = new List<Citation>()
            }, response.TotalTokens);
        }

        try
        {
            // Attempt JSON parse
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                bool isRefusal = root.TryGetProperty("isRefusal", out var rProp) && rProp.GetBoolean();
                string? refusalReason = root.TryGetProperty("refusalReason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String ? reasonProp.GetString() : null;

                if (isRefusal)
                {
                    return (new InquiryDraft
                    {
                        IsRefusal = true,
                        RefusalReason = refusalReason ?? StandardRefusalPhrase,
                        EligibilitySummary = StandardRefusalPhrase,
                        ProcedureSteps = StandardRefusalPhrase,
                        RequiredDocuments = StandardRefusalPhrase,
                        FeesAndTimeline = StandardRefusalPhrase,
                        Citations = new List<Citation>()
                    }, response.TotalTokens);
                }

                string elig = root.TryGetProperty("eligibility", out var eProp) ? eProp.GetString() ?? "" : "";
                string docs = root.TryGetProperty("requiredDocuments", out var dProp) ? dProp.GetString() ?? "" : "";
                string steps = root.TryGetProperty("procedureSteps", out var sProp) ? sProp.GetString() ?? "" : "";
                string fees = root.TryGetProperty("feesAndTimeline", out var fProp) ? fProp.GetString() ?? "" : "";

                return (new InquiryDraft
                {
                    IsRefusal = false,
                    EligibilitySummary = elig,
                    RequiredDocuments = docs,
                    ProcedureSteps = steps,
                    FeesAndTimeline = fees,
                    Citations = citations
                }, response.TotalTokens);
            }
        }
        catch
        {
            // Fallback to formatted text response
        }

        return (new InquiryDraft
        {
            IsRefusal = false,
            EligibilitySummary = eligibilitySummary,
            ProcedureSteps = procedureSummary,
            RequiredDocuments = procedureSummary,
            FeesAndTimeline = procedureSummary,
            Citations = citations
        }, response.TotalTokens);
    }
}
