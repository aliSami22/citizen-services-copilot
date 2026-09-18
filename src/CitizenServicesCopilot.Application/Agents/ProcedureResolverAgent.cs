using System.Text;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Agents;

public class ProcedureResolverAgent
{
    private readonly ILLMProvider _llmProvider;

    public ProcedureResolverAgent(ILLMProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public async Task<(string RequiredDocs, string Steps, string FeesAndTimeline, int TokensUsed)> ResolveProcedureAsync(
        string question,
        IReadOnlyList<DocumentChunk> contextChunks,
        string modelName,
        CancellationToken ct = default)
    {
        if (contextChunks.Count == 0)
        {
            return ("No regulatory documentation provided.", "No procedures available.", "No fee schedule available.", 0);
        }

        var sb = new StringBuilder();
        sb.AppendLine("You are the specialized 'Procedure Resolver Agent' for Government Citizen Services.");
        sb.AppendLine("Analyze ONLY the provided regulatory excerpts below. Extract:");
        sb.AppendLine("1. Required original and photocopy documents.");
        sb.AppendLine("2. Step-by-step application and submission procedure.");
        sb.AppendLine("3. Applicable fees, payment methods, and turnaround timeline.");
        sb.AppendLine("CRITICAL RULE: Rely ONLY on provided excerpts. Do NOT assume procedures or fees not explicitly stated.");
        sb.AppendLine();
        sb.AppendLine("--- EXCERPTS ---");
        foreach (var chunk in contextChunks)
        {
            sb.AppendLine($"[Document: {chunk.Document?.Title ?? "Official Decree"} | Page: {chunk.PageNumber} | Section: {chunk.Section}]");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }

        var prompt = new LlmPrompt(
            Messages: new List<LlmMessage>
            {
                new("system", sb.ToString()),
                new("user", $"Citizen Question: {question}\nFormat your answer clearly with sections: 'Required Documents', 'Step-by-Step Procedure', 'Fees and Timeline'.")
            },
            ModelName: modelName,
            Temperature: 0.0f,
            MaxTokens: 500
        );

        var response = await _llmProvider.GenerateCompletionAsync(prompt, ct);
        var content = response.Content.Trim();

        // Parse sections heuristically or preserve formatted text
        return (
            RequiredDocs: content,
            Steps: content,
            FeesAndTimeline: content,
            TokensUsed: response.TotalTokens
        );
    }
}
