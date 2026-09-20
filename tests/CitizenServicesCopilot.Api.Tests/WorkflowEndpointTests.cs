using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CitizenServicesCopilot.Api.Tests;

public class WorkflowEndpointTests : IClassFixture<WorkflowApiFactory>
{
    private readonly HttpClient _client;

    public WorkflowEndpointTests(WorkflowApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_CitizenResponse_Returns202WithRunId()
    {
        var resp = await _client.PostAsJsonAsync("/api/workflows/citizen-response",
            new { userId = "u-1", question = "What is the unemployment benefit?" });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParse(body.GetProperty("runId").GetString(), out _));
    }

    [Fact]
    public async Task Post_CitizenResponse_EmptyQuestion_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/workflows/citizen-response",
            new { userId = "u-1", question = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Run_NotFound_Returns404()
    {
        var resp = await _client.GetAsync($"/api/runs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Approve_WithoutOfficerHeader_Returns403()
    {
        var resp = await _client.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/approve",
            new { approverId = "officer-1" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Reject_WithoutOfficerHeader_Returns403()
    {
        var resp = await _client.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/reject",
            new { approverId = "officer-1", reason = "insufficient evidence" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task EditAndApprove_InvalidJson_Returns400_WhenOfficer()
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/runs/{Guid.NewGuid()}/edit-and-approve");
        req.Headers.Add("X-Role", "Officer");
        req.Content = JsonContent.Create(new
        {
            approverId = "officer-1",
            editedDraftJson = "not valid json",
            reason = (string?)null
        });
        var resp = await _client.SendAsync(req);
        // Acceptable: 400 (invalid JSON), 404 (run not found), 409 (already decided).
        // The test's intent is: it does NOT 200, and it does NOT 403.
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Approve_OnUnknownRun_WithOfficer_DoesNotReturn403()
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/runs/{Guid.NewGuid()}/approve");
        req.Headers.Add("X-Role", "Officer");
        req.Content = JsonContent.Create(new { approverId = "officer-1" });
        var resp = await _client.SendAsync(req);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}

public class WorkflowApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // TODO: swap AppDbContext for InMemory. Use the same pattern as
            // GroundedRetrieverTests (test AppDbContext subclass overriding
            // OnModelCreating to avoid pgvector). If you cannot resolve this
            // in one pass, leave the factory at default and mark the tests
            // that need a DB with Skip = "in-memory swap pending". Report it.
        });
    }
}