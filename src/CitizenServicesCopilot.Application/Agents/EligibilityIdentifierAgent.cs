using System.Text;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;

namespace CitizenServicesCopilot.Application.Agents;

public class EligibilityIdentifierAgent
{
    private readonly ILLMProvider _llmProvider;

    public EligibilityIdentifierAgent(ILLMProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public async Task<(string Summary, int TokensUsed)> IdentifyEligibilityAsync(
        string question,
        IReadOnlyList<DocumentChunk> contextChunks,
        string modelName,
        CancellationToken ct = default)
    {
        if (contextChunks.Count == 0)
        {
            return ("No regulatory documentation provided for eligibility analysis.", 0);
        }

        var sb = new StringBuilder();
        sb.AppendLine("You are the specialized 'Eligibility Identifier Agent' for Government Citizen Services.");
        sb.AppendLine("Analyze ONLY the provided legal/regulatory excerpts below. Identify:");
        sb.AppendLine("1. Exact eligibility criteria and prerequisites.");
        sb.AppendLine("2. Disqualifying conditions or restrictions.");
        sb.AppendLine("CRITICAL RULE: Rely ONLY on the provided excerpts. Do NOT extrapolate or assume external regulations.");
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
                new("user", $"Citizen Question: {question}\nProvide the eligibility summary strictly based on the text.")
            },
            ModelName: modelName,
            Temperature: 0.0f,
            MaxTokens: 400
        );

        var response = await _llmProvider.GenerateCompletionAsync(prompt, ct);
        return (response.Content.Trim(), response.TotalTokens);
    }
}
