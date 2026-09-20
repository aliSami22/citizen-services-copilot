using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CitizenServicesCopilot.Domain.Workflows;
using CitizenServicesCopilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CitizenServicesCopilot.Api.Tests;

public class OpsEndpointsTests : IClassFixture<WorkflowApiFactory>
{
    private readonly WorkflowApiFactory _factory;

    public OpsEndpointsTests(WorkflowApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_Returns200()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_Returns200_WhenStoreResponds()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/ready");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ready", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Trace_ReturnsRunCorrelation_WhenHeaderPropagates()
    {
        var correlationId = Guid.NewGuid();
        using var citizen = await _factory.CreateAuthenticatedClientAsync("u-trace", "Citizen");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/workflows/citizen-response")
        {
            Content = JsonContent.Create(new { question = "Voter registration?" })
        };
        req.Headers.Add("X-Correlation-Id", correlationId.ToString());

        var resp = await citizen.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var runId = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetString()!;

        // The run terminates quickly on an empty corpus (retrieval refusal).
        var body = JsonSerializer.Deserialize<JsonElement>(await PollForTerminalTraceAsync(citizen, runId));

        Assert.Equal(runId, body.GetProperty("runId").GetString());
        Assert.Equal(correlationId, body.GetProperty("correlationId").GetGuid());
        Assert.Equal("Failed", body.GetProperty("status").GetString());
        Assert.True(body.TryGetProperty("steps", out var steps), "trace must include a steps array");
        Assert.True(steps.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task Trace_AsDifferentCitizen_Returns403()
    {
        var owner = $"u-towner-{Guid.NewGuid():N}";
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = WorkflowRun.Create(owner);
            db.WorkflowRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var stranger = await _factory.CreateAuthenticatedClientAsync("u-stranger-2", "Citizen");
        var resp = await stranger.GetAsync($"/api/runs/{runId}/trace");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Trace_NotFound_Returns404()
    {
        var officer = await _factory.CreateAuthenticatedClientAsync("o-trace", "Officer");
        var resp = await officer.GetAsync($"/api/runs/{Guid.NewGuid()}/trace");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private async Task<string> PollForTerminalTraceAsync(HttpClient client, string runId)
    {
        for (var i = 0; i < 60; i++)
        {
            var resp = await client.GetAsync($"/api/runs/{runId}/trace");
            var body = await resp.Content.ReadAsStringAsync();
            resp.Dispose();

            // Only a 200 carries "status" (ends after the 4xx/5xx body shape).
            var status = body.Contains("\"status\"") &&
                         JsonSerializer.Deserialize<JsonElement>(body).TryGetProperty("status", out var s)
                ? s.GetString()
                : null;
            if (status is "Approved" or "Rejected" or "Failed" or "Cancelled")
            {
                return body;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"run {runId} did not reach a terminal state");
    }
}

/// <summary>
/// Request correlation reaching logger scopes can only be observed when a run
/// actually reaches an LLM call (the empty-corpus refusal path fails before the
/// scoped section), so this uses the deterministic success-stub factory.
/// </summary>
public class OpsCorrelationScopeTests : IClassFixture<StreamWorkflowApiFactory>
{
    private readonly StreamWorkflowApiFactory _factory;

    public OpsCorrelationScopeTests(StreamWorkflowApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Correlation_Header_ReachesLoggerScopes_OnSuccessPath()
    {
        var correlationId = Guid.NewGuid();
        using var citizen = await _factory.CreateAuthenticatedClientAsync("u-logscope", "Citizen");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/workflows/citizen-response")
        {
            Content = JsonContent.Create(new { question = "Passport procedure?" })
        };
        req.Headers.Add("X-Correlation-Id", correlationId.ToString());

        var resp = await citizen.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var runId = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetString()!;

        string status = "Created";
        for (var i = 0; i < 40; i++)
        {
            using var poll = await citizen.GetAsync($"/api/runs/{runId}");
            if (poll.StatusCode == HttpStatusCode.OK)
            {
                var body = await poll.Content.ReadAsStringAsync();
                status = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("status").GetString() ?? status;
            }

            if (status is "Approved" or "Rejected" or "Failed" or "Cancelled")
            {
                break;
            }

            await Task.Delay(250);
        }

        var capture = _factory.Services.GetRequiredService<ScopeCapturingLoggerProvider>();
        Assert.True(capture.ContainsCorrelation(correlationId),
            $"no log scope carried the request's correlation id (run {runId} ended {status}; {capture.RecordCount} scope records)");
    }
}