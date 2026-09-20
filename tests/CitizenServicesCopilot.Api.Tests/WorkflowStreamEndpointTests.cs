using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CitizenServicesCopilot.Application.Orchestration;
using CitizenServicesCopilot.Domain.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CitizenServicesCopilot.Api.Tests;

public class StreamDiProbeTests : IClassFixture<StreamWorkflowApiFactory>
{
    private readonly StreamWorkflowApiFactory _factory;

    public StreamDiProbeTests(StreamWorkflowApiFactory factory) => _factory = factory;

    /// <summary>
    /// Regression guard: a full run through DI + EF in-memory repositories must
    /// reach a terminal state. Covers the tracked-instance fix in
    /// EfWorkflowRunRepository.UpdateAsync (Add-then-Update with record `with`
    /// clones used to throw "already being tracked").
    /// </summary>
    [Fact]
    public async Task Run_ThroughDi_CompletesToApprovalTimeout()
    {
        using var scope = _factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<WorkflowOrchestrator>();
        var run = await orchestrator.RunAsync("probe", "q", "gpt-4o-mini");
        Assert.NotNull(run);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("approval timeout", run.ErrorMessage);
    }
}

/// <summary>
/// Server-Sent Events streaming tests for GET /api/workflows/stream.
/// A full run publishes stage/step/error/done events; a client disconnect
/// cancels the run and persists no further steps.
/// </summary>
public class WorkflowStreamEndpointTests : IClassFixture<StreamWorkflowApiFactory>
{
    private readonly HttpClient _client;

    public WorkflowStreamEndpointTests(StreamWorkflowApiFactory factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Get_WorkflowStream_EmitsStageStepAndDone()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var resp = await _client.GetAsync(
            $"/api/workflows/stream?userId=u-stream&question={Uri.EscapeDataString("Passport procedure?")}",
            timeout.Token);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);

        var events = new List<JsonElement>();
        using (var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(timeout.Token)))
        {
            string? line;
            while ((line = await ReadLineAsync(reader, timeout.Token)) is not null)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var evt = JsonSerializer.Deserialize<JsonElement>(line["data:".Length..]);
                events.Add(evt);
                if (IsType(evt, "done"))
                {
                    break;
                }
            }
        }

        var staged = events.Where(e => IsType(e, "stage"))
            .Select(e => e.GetProperty("stage").GetString()).ToList();
        Assert.Contains("EligibilityIdentifier", staged);
        Assert.Contains("ProcedureResolver", staged);
        Assert.Contains("ResponseDrafter", staged);

        Assert.Contains(events,
            e => IsType(e, "step") && Get(e, "stage") == "EligibilityIdentifier" && Get(e, "status") == "Succeeded");
        Assert.Contains(events,
            e => IsType(e, "step") && Get(e, "stage") == "ProcedureResolver" && Get(e, "status") == "Succeeded");
        Assert.Contains(events,
            e => IsType(e, "step") && Get(e, "stage") == "ResponseDrafter" && Get(e, "status") == "Succeeded");

        var runId = events[0].GetProperty("runId").GetGuid();
        Assert.All(events, e => Assert.Equal(runId, e.GetProperty("runId").GetGuid()));

        Assert.Contains(events, e => IsType(e, "error") && Get(e, "message") == "approval timeout");
        Assert.Contains(events, e => IsType(e, "done") && Get(e, "status") == "Failed");
    }

    private static bool IsType(JsonElement evt, string type)
        => evt.GetProperty("type").GetString() == type;

    private static string? Get(JsonElement evt, string property)
        => evt.GetProperty(property).GetString();

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
        => await reader.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(20), ct);
}

/// <summary>
/// Streaming with a shared LLM that blocks on its second call (the procedure
/// stage). Cancelling the request must cancel the run and leave only the
/// eligibility step persisted.
/// </summary>
public class WorkflowStreamCancelTests : IClassFixture<StreamWorkflowCancelApiFactory>
{
    private readonly HttpClient _client;
    private readonly StreamWorkflowCancelApiFactory _factory;

    public WorkflowStreamCancelTests(StreamWorkflowCancelApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_WorkflowStream_ClientCancel_StopsRun_WithNoFurtherSteps()
    {
        var userId = $"u-cancel-{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"/api/workflows/stream?userId={userId}&question={Uri.EscapeDataString("Passport procedure?")}");
        var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Guid runId = Guid.Empty;
        bool sawEligibilityStep = false;
        using (var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(cts.Token)))
        {
            try
            {
                string? line;
                while ((line = await ReadLineAsync(reader, cts.Token)) is not null)
                {
                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var evt = JsonSerializer.Deserialize<JsonElement>(line["data:".Length..]);
                    if (runId == Guid.Empty)
                    {
                        runId = evt.GetProperty("runId").GetGuid();
                    }

                    if (IsType(evt, "step") && Get(evt, "stage") == "EligibilityIdentifier")
                    {
                        sawEligibilityStep = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The read token may race the server closing the connection.
            }
        }

        Assert.True(sawEligibilityStep, "expected the eligibility step event before cancellation");

        // Client disconnects; the server aborts the run through requestAborted.
        cts.Cancel();
        resp.Dispose();

        await WaitForRunAsync(runId);
    }

    private async Task WaitForRunAsync(Guid runId)
    {
        // The run must reach Cancelled and persist exactly one step
        // (eligibility) - nothing after cancellation. Poll until a terminal
        // status (or budget) rather than snapping the first visible row: the
        // server-side abort completes concurrently with this poll.
        var status = "Created";
        HttpResponseMessage? resp = null;
        for (var i = 0; i < 60; i++)
        {
            resp = await _client.GetAsync($"/api/runs/{runId}");
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var body = await resp.Content.ReadAsStringAsync();
                status = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("status").GetString();
                if (status is not ("Pending" or "Running" or "Created"))
                {
                    break;
                }
            }

            await Task.Delay(200);
            resp.Dispose();
        }

        Assert.NotNull(resp);
        Assert.Equal(HttpStatusCode.OK, resp!.StatusCode);
        Assert.Equal("Cancelled", status);

        var body2 = await (await _client.GetAsync($"/api/runs/{runId}")).Content.ReadAsStringAsync();
        var run = JsonSerializer.Deserialize<JsonElement>(body2);
        var steps = run.GetProperty("steps");
        Assert.Equal(1, steps.GetArrayLength());
        Assert.Equal("EligibilityIdentifier", steps[0].GetProperty("role").GetString());
    }

    private static bool IsType(JsonElement evt, string type)
        => evt.GetProperty("type").GetString() == type;

    private static string? Get(JsonElement evt, string property)
        => evt.GetProperty(property).GetString();

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
        => await reader.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(20), ct);
}