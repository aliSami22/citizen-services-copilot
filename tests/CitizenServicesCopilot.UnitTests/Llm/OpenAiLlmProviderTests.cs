using System.Net;
using System.Text;
using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;

namespace CitizenServicesCopilot.UnitTests.Llm;

/// <summary>
/// Guards the provider-side tier resolution: the router may hand the provider
/// a tier keyword ("cheap"/"premium") instead of a concrete model name, and the
/// provider must map it to the model configured for the current provider.
/// </summary>
public class OpenAiLlmProviderTests
{
    [Fact]
    public async Task GenerateCompletionAsync_CheapTier_ResolvesConfiguredCheapModel()
    {
        var handler = new StubChatHandler();
        var provider = CreateProvider(handler);

        await provider.GenerateCompletionAsync(Prompt("cheap"));

        Assert.Equal("gm-cheap", CapturedModel(handler.LastRequestBody!));
    }

    [Fact]
    public async Task GenerateCompletionAsync_PremiumTier_ResolvesConfiguredExpensiveModel()
    {
        var handler = new StubChatHandler();
        var provider = CreateProvider(handler);

        await provider.GenerateCompletionAsync(Prompt("premium"));

        Assert.Equal("gm-premium", CapturedModel(handler.LastRequestBody!));
    }

    [Fact]
    public async Task GenerateCompletionAsync_ExplicitModelName_PassesThrough()
    {
        var handler = new StubChatHandler();
        var provider = CreateProvider(handler);

        await provider.GenerateCompletionAsync(Prompt("gemini-2.5-flash"));

        Assert.Equal("gemini-2.5-flash", CapturedModel(handler.LastRequestBody!));
    }

    private static OpenAiLlmProvider CreateProvider(StubChatHandler handler)
        => new(
            new HttpClient(handler),
            new OpenAiConfig
            {
                ApiKey = "test-key-not-a-real-secret",
                CheapModel = "gm-cheap",
                ExpensiveModel = "gm-premium"
            },
            NullLogger<OpenAiLlmProvider>.Instance);

    private static LlmPrompt Prompt(string modelName)
        => new(
            new LlmMessage[] { new("user", "hello") },
            modelName);

    private static string CapturedModel(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        return doc.RootElement.GetProperty("model").GetString()!;
    }

    private sealed class StubChatHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            const string responseJson = """
                {
                  "choices": [ { "index": 0, "message": { "role": "assistant", "content": "ok" } } ],
                  "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}