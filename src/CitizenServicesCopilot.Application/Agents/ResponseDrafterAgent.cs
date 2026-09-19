using System.Text;
using System.Text.Json;
using CitizenServicesCopilot.Application.Common;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Prompts;
using CitizenServicesCopilot.Application.Services.Tools;
using CitizenServicesCopilot.Domain.Agents;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.ValueObjects;
using CitizenServicesCopilot.Domain.Workflows;
using Citation = CitizenServicesCopilot.Domain.ValueObjects.Citation;

namespace CitizenServicesCopilot.Application.Agents;

public class ResponseDrafterAgent : IAgent
{
    private readonly ILLMProvider _llmProvider;
    private readonly IPromptProvider _promptProvider;

    public const string StandardRefusalPhrase = "Not enough information in the corpus";

    public AgentRole Role => AgentRole.ResponseDrafter;

    public IReadOnlySet<string> AllowedTools { get; } = new HashSet<string>
    {
        ToolCatalog.SearchCorpus,
        ToolCatalog.ComputeFee
    };

    public ResponseDrafterAgent(ILLMProvider llmProvider, IPromptProvider promptProvider)
    {
        _llmProvider = llmProvider;
        _promptProvider = promptProvider;
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

        var draft = BuildDraft(content, contextChunks, response.TotalTokens, eligibilitySummary, procedureSummary);

        return (ToInquiryDraft(draft), draft.TokensUsed);
    }

    public async Task<AgentStep> ExecuteAsync(AgentInput input, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (input.ContextChunks.Count == 0)
        {
            return new AgentStep(
                Role,
                AgentStepStatus.Failed,
                startedAt,
                DateTimeOffset.UtcNow,
                null,
                null,
                "No evidence to draft a grounded response.");
        }

        try
        {
            var eligibilitySummary = ExtractPriorOutput(input.PriorSteps, AgentRole.EligibilityIdentifier);
            var procedureSummary = ExtractPriorOutput(input.PriorSteps, AgentRole.ProcedureResolver);

            var template = await _promptProvider.GetPromptAsync(PromptKeys.ResponseDrafter, ct);
            var prompt = template
                .Replace("{context}", PromptContextBuilder.FormatChunks(input.ContextChunks))
                .Replace("{query}", input.Query)
                .Replace("{eligibility_summary}", eligibilitySummary)
                .Replace("{procedure_summary}", procedureSummary);

            var llmPrompt = new LlmPrompt(
                Messages: new List<LlmMessage>
                {
                    new("system", prompt),
                    new("user", $"Citizen Question: {input.Query}\nSynthesize the final JSON draft response.")
                },
                ModelName: input.ModelName,
                Temperature: 0.0f,
                MaxTokens: 800
            );

            var response = await _llmProvider.GenerateCompletionAsync(llmPrompt, ct);

            var draft = BuildDraft(
                response.Content.Trim(),
                input.ContextChunks,
                response.TotalTokens,
                eligibilitySummary,
                procedureSummary);

            var draftJson = JsonSerializer.Serialize(draft, JsonOptions.CamelCase);

            return new AgentStep(
                Role,
                AgentStepStatus.Succeeded,
                startedAt,
                DateTimeOffset.UtcNow,
                draft.TokensUsed,
                draftJson,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AgentStep(Role, AgentStepStatus.Failed, startedAt, DateTimeOffset.UtcNow, null, null, ex.Message);
        }
    }

    private static string ExtractPriorOutput(IReadOnlyList<AgentStep> priorSteps, AgentRole role)
        => priorSteps
            .FirstOrDefault(step => step.Role == role && step.Status == AgentStepStatus.Succeeded && step.OutputSummary is not null)
            ?.OutputSummary ?? string.Empty;

    private static DraftResponse BuildDraft(
        string content,
        IReadOnlyList<DocumentChunk> contextChunks,
        int tokensUsed,
        string eligibilitySummary,
        string procedureSummary)
    {
        var citations = contextChunks.Select(c => new Citation(
            DocumentTitle: c.Document?.Title ?? "Official Decree",
            Source: c.Document?.Source ?? "Official Gazette",
            PageNumber: c.PageNumber,
            Section: c.Section,
            QuoteSnippet: c.Content.Length > 160 ? c.Content[..160] + "..." : c.Content
        )).ToList();

        if (content.Contains(StandardRefusalPhrase, StringComparison.OrdinalIgnoreCase))
        {
            return NewRefusal(tokensUsed);
        }

        try
        {
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
                    return NewRefusal(tokensUsed, refusalReason ?? StandardRefusalPhrase);
                }

                string elig = root.TryGetProperty("eligibility", out var eProp) ? eProp.GetString() ?? "" : "";
                string docs = root.TryGetProperty("requiredDocuments", out var dProp) ? dProp.GetString() ?? "" : "";
                string steps = root.TryGetProperty("procedureSteps", out var sProp) ? sProp.GetString() ?? "" : "";
                string fees = root.TryGetProperty("feesAndTimeline", out var fProp) ? fProp.GetString() ?? "" : "";

                return new DraftResponse(
                    IsRefusal: false,
                    RefusalReason: null,
                    EligibilitySummary: elig,
                    RequiredDocuments: docs,
                    ProcedureSteps: steps,
                    FeesAndTimeline: fees,
                    Citations: citations,
                    TokensUsed: tokensUsed);
            }
        }
        catch
        {
            // Fallback to formatted text response
        }

        return new DraftResponse(
            IsRefusal: false,
            RefusalReason: null,
            EligibilitySummary: eligibilitySummary,
            RequiredDocuments: procedureSummary,
            ProcedureSteps: procedureSummary,
            FeesAndTimeline: procedureSummary,
            Citations: citations,
            TokensUsed: tokensUsed);
    }

    private static DraftResponse NewRefusal(int tokensUsed, string? reason = null)
        => new(
            IsRefusal: true,
            RefusalReason: reason ?? StandardRefusalPhrase,
            EligibilitySummary: StandardRefusalPhrase,
            RequiredDocuments: StandardRefusalPhrase,
            ProcedureSteps: StandardRefusalPhrase,
            FeesAndTimeline: StandardRefusalPhrase,
            Citations: new List<Citation>(),
            TokensUsed: tokensUsed);

    private static InquiryDraft ToInquiryDraft(DraftResponse draft)
        => new()
        {
            IsRefusal = draft.IsRefusal,
            RefusalReason = draft.RefusalReason,
            EligibilitySummary = draft.EligibilitySummary,
            ProcedureSteps = draft.ProcedureSteps,
            RequiredDocuments = draft.RequiredDocuments,
            FeesAndTimeline = draft.FeesAndTimeline,
            Citations = new List<Citation>(draft.Citations)
        };
}