using System.Text;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Prompts;
using CitizenServicesCopilot.Application.Services.Tools;
using CitizenServicesCopilot.Domain.Agents;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.Application.Agents;

public class ProcedureResolverAgent : IAgent
{
    private readonly ILLMProvider _llmProvider;
    private readonly IPromptProvider _promptProvider;

    public AgentRole Role => AgentRole.ProcedureResolver;

    public IReadOnlySet<string> AllowedTools { get; } = new HashSet<string> { ToolCatalog.GetRegulationVersion };

    public ProcedureResolverAgent(ILLMProvider llmProvider, IPromptProvider promptProvider)
    {
        _llmProvider = llmProvider;
        _promptProvider = promptProvider;
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
                "No regulatory documentation provided for procedure resolution.");
        }

        try
        {
            var template = await _promptProvider.GetPromptAsync(PromptKeys.ProcedureResolver, ct);
            var prompt = template
                .Replace("{context}", PromptContextBuilder.FormatChunks(input.ContextChunks))
                .Replace("{query}", input.Query);

            var llmPrompt = new LlmPrompt(
                Messages: new List<LlmMessage>
                {
                    new("system", prompt),
                    new("user", $"Citizen Question: {input.Query}\nFormat your answer clearly with sections: 'Required Documents', 'Step-by-Step Procedure', 'Fees and Timeline'.")
                },
                ModelName: input.ModelName,
                Temperature: 0.0f,
                MaxTokens: 500
            );

            var response = await _llmProvider.GenerateCompletionAsync(llmPrompt, ct);

            var plan = new ProcedurePlan(
                RequiredDocuments: response.Content.Trim(),
                Steps: response.Content.Trim(),
                FeesAndTimeline: response.Content.Trim(),
                TokensUsed: response.TotalTokens);

            var summary = $"{plan.RequiredDocuments}\n{plan.Steps}\n{plan.FeesAndTimeline}".Trim();

            return new AgentStep(
                Role,
                AgentStepStatus.Succeeded,
                startedAt,
                DateTimeOffset.UtcNow,
                plan.TokensUsed,
                summary,
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
}