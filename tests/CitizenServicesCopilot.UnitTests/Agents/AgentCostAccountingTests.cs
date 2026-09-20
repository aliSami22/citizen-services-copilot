using CitizenServicesCopilot.Application.Agents;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Agents;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Workflows;
using Microsoft.Extensions.Logging.Abstractions;

namespace CitizenServicesCopilot.UnitTests.Agents;

public class AgentCostAccountingTests
{
    [Fact]
    public async Task EligibilityAgent_RecordsTokensInOutAndCostOnStep()
    {
        var agent = new EligibilityIdentifierAgent(
            new StubLlmProvider(new LlmResponse("resident of the emirate", PromptTokens: 200, CompletionTokens: 40, TotalTokens: 240, "gpt-4o-mini")),
            new StubPromptProvider("{context}\n{query}"),
            new StubCorrelationContext(Guid.NewGuid()),
            NullLogger<EligibilityIdentifierAgent>.Instance);

        var step = await agent.ExecuteAsync(Input("gpt-4o-mini"), CancellationToken.None);

        Assert.Equal(AgentStepStatus.Succeeded, step.Status);
        Assert.Equal(200, step.TokensIn);
        Assert.Equal(40, step.TokensOut);
        // cheap tier: 200*0.00015/1000 + 40*0.00060/1000
        Assert.Equal(0.000054m, step.CostUsd);
    }

    [Fact]
    public async Task DrafterAgent_RecordsTokensInOutAndCostOnStep()
    {
        var agent = new ResponseDrafterAgent(
            new StubLlmProvider(new LlmResponse("draft text", PromptTokens: 250, CompletionTokens: 70, TotalTokens: 320, "gpt-4o")),
            new StubPromptProvider("{context}\n{query}\n{eligibility_summary}\n{procedure_summary}"),
            new StubCorrelationContext(Guid.NewGuid()),
            NullLogger<ResponseDrafterAgent>.Instance);

        var step = await agent.ExecuteAsync(Input("gpt-4o"), CancellationToken.None);

        Assert.Equal(AgentStepStatus.Succeeded, step.Status);
        Assert.Equal(250, step.TokensIn);
        Assert.Equal(70, step.TokensOut);
        // premium tier: 250*0.0025/1000 + 70*0.01/1000
        Assert.Equal(0.001325m, step.CostUsd);
    }

    private static AgentInput Input(string modelName) => new(
        RunId: Guid.NewGuid(),
        UserId: "user-1",
        Query: "How do I get a passport?",
        ModelName: modelName,
        ContextChunks: new DocumentChunk[] { new() { Content = "passport requires residency", PageNumber = 1, Section = "S1" } },
        PriorSteps: Array.Empty<AgentStep>());

    private sealed class StubLlmProvider : ILLMProvider
    {
        private readonly LlmResponse _response;

        public string ProviderName => "stub";

        public StubLlmProvider(LlmResponse response) => _response = response;

        public Task<LlmResponse> GenerateCompletionAsync(LlmPrompt prompt, CancellationToken ct = default)
            => Task.FromResult(_response);
    }

    private sealed class StubPromptProvider : IPromptProvider
    {
        private readonly string _template;

        public StubPromptProvider(string template) => _template = template;

        public Task<string> GetPromptAsync(string promptKey, CancellationToken ct = default)
            => Task.FromResult(_template);

        public Task<string> GetPromptVersionAsync(string promptKey, CancellationToken ct = default)
            => Task.FromResult("v1");
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public StubCorrelationContext(Guid correlationId) => CorrelationId = correlationId;

        public Guid CorrelationId { get; set; }
    }
}