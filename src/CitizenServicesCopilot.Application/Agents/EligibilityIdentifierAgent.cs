using System.Text;
using CitizenServicesCopilot.Application.Common;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Prompts;
using CitizenServicesCopilot.Domain.Agents;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Workflows;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Application.Agents;

public class EligibilityIdentifierAgent : IAgent
{
    private readonly ILLMProvider _llmProvider;
    private readonly IPromptProvider _promptProvider;
    private readonly ICorrelationContext _correlation;
    private readonly ILogger<EligibilityIdentifierAgent> _logger;

    public AgentRole Role => AgentRole.EligibilityIdentifier;

    public IReadOnlySet<string> AllowedTools { get; } = new HashSet<string>();

    public EligibilityIdentifierAgent(
        ILLMProvider llmProvider,
        IPromptProvider promptProvider,
        ICorrelationContext correlation,
        ILogger<EligibilityIdentifierAgent> logger)
    {
        _llmProvider = llmProvider;
        _promptProvider = promptProvider;
        _correlation = correlation;
        _logger = logger;
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

    public async Task<AgentStep> ExecuteAsync(AgentInput input, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (input.ContextChunks.Count == 0)
        {
            return new AgentStep(
                Role: Role,
                Status: AgentStepStatus.Failed,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                OutputSummary: null,
                ErrorMessage: "No regulatory documentation provided for eligibility analysis.");
        }

        try
        {
            var template = await _promptProvider.GetPromptAsync(PromptKeys.EligibilityIdentifier, ct);
            var prompt = template
                .Replace("{context}", PromptContextBuilder.FormatChunks(input.ContextChunks))
                .Replace("{query}", input.Query);

            var llmPrompt = new LlmPrompt(
                Messages: new List<LlmMessage>
                {
                    new("system", prompt),
                    new("user", $"Citizen Question: {input.Query}\nProvide the eligibility summary strictly based on the text.")
                },
                ModelName: input.ModelName,
                Temperature: 0.0f,
                MaxTokens: 400
            );

            LlmResponse response;
            using (_logger.BeginScope(new { CorrelationId = _correlation.CorrelationId }))
            {
                response = await _llmProvider.GenerateCompletionAsync(llmPrompt, ct);
            }

            var result = new EligibilityResult(response.Content.Trim(), response.TotalTokens);

            return new AgentStep(
                Role: Role,
                Status: AgentStepStatus.Succeeded,
                CreatedAtUtc: startedAt,
                OutputSummary: result.Summary,
                TokensIn: response.PromptTokens > 0 ? response.PromptTokens : null,
                TokensOut: response.CompletionTokens > 0 ? response.CompletionTokens : null,
                CostUsd: CostEstimator.CalculateActualCost(
                    CostEstimator.IsCheapModel(input.ModelName), response.PromptTokens, response.CompletionTokens));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AgentStep(Role: Role, Status: AgentStepStatus.Failed, CreatedAtUtc: DateTimeOffset.UtcNow, ErrorMessage: ex.Message);
        }
    }
}