using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CitizenServicesCopilot.Api.Tests;

/// <summary>
/// Api test factories with deterministic data paths for the SSE stream:
/// swap the real LLM + retrieval for fast, offline stubs and shorten the
/// approval window so a full run terminates promptly.
/// </summary>
public class StreamWorkflowApiFactory : WorkflowApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Orchestrator:ApprovalWaitTimeout", "00:00:02");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILLMProvider>();
            services.AddScoped<ILLMProvider>(_ => new AlwaysSucceedLlmProvider());
            services.RemoveAll<IRetrievalService>();
            services.AddScoped<IRetrievalService>(_ => TestRetrieval.Success());
        });
    }
}

/// <summary>
/// Consumer of the same work queue; the LLM blocks on its second invocation
/// (the procedure stage) until the request cancellation token fires.
/// </summary>
public class StreamWorkflowCancelApiFactory : WorkflowApiFactory
{
    public BlockingLlmProvider Llm { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILLMProvider>();
            services.AddScoped<ILLMProvider>(_ => Llm);
            services.RemoveAll<IRetrievalService>();
            services.AddScoped<IRetrievalService>(_ => TestRetrieval.Success());
        });
    }
}

/// <summary>
/// Returns a fixed, well-formed completion for every stage so a stream test
/// can observe the full stage/step/done sequence without a network.
/// </summary>
public sealed class AlwaysSucceedLlmProvider : ILLMProvider
{
    public string ProviderName => "stub";

    public Task<LlmResponse> GenerateCompletionAsync(LlmPrompt prompt, CancellationToken ct = default)
        => Task.FromResult(new LlmResponse(
            "Eligible. Step-by-step procedure and applicable fees documented.",
            120, 40, 160, prompt.ModelName));
}

/// <summary>
/// Succeeds once (eligibility) then blocks until cancelled on every further
/// call, so the run pauses deterministically at the procedure stage.
/// </summary>
public sealed class BlockingLlmProvider : ILLMProvider
{
    private int _calls;

    public string ProviderName => "stub";

    /// <summary>Completes when the run has entered the blocking call.</summary>
    public TaskCompletionSource EnteredBlockingCall { get; }
        = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<LlmResponse> GenerateCompletionAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        var call = Interlocked.Increment(ref _calls);
        if (call == 1)
        {
            return Task.FromResult(new LlmResponse(
                "Eligible.", 120, 40, 160, prompt.ModelName));
        }

        EnteredBlockingCall.TrySetResult();
        return BlockAsync(ct);
    }

    private static async Task<LlmResponse> BlockAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        return new LlmResponse("unreachable", 0, 0, 0, "stub");
    }
}

internal static class TestRetrieval
{
    public static IRetrievalService Success()
    {
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            Content = "Passport applications require residency and a completed form.",
            PageNumber = 1,
            Section = "Chapter 1"
        };

        return new StubRetrieval(RetrievalResult.Success(
            new[] { new ScoredChunk(chunk, DenseScore: 0.9, KeywordScore: 0.8, CombinedScore: 0.85) },
            Array.Empty<Citation>(),
            0.85));
    }

    private sealed class StubRetrieval : IRetrievalService
    {
        private readonly RetrievalResult _result;

        public StubRetrieval(RetrievalResult result) => _result = result;

        public Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default)
            => Task.FromResult(_result);
    }
}