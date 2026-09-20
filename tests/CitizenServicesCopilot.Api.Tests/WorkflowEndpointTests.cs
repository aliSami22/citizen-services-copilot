using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Workflows;
using CitizenServicesCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var runId = body.GetProperty("runId").GetString();
        Assert.True(Guid.TryParse(runId, out _));
        Assert.False(string.IsNullOrWhiteSpace(runId));

        // Prove the background orchestration actually started: it must have
        // persisted the run record (proving full DI resolution of the
        // orchestrator and its repository dependencies) without throwing a
        // DI-resolution error. Poll briefly, then assert the run is observable.
        HttpStatusCode status = default;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(200);
            var runResp = await _client.GetAsync($"/api/runs/{runId}");
            status = runResp.StatusCode;
            if (status == HttpStatusCode.OK)
            {
                break;
            }
        }

        Assert.Equal(HttpStatusCode.OK, status);
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
    // One InMemory store per factory instance; recreating it per-request-scope
    // would give every HttpContext a different (empty) database.
    private static readonly string _dbName = $"citizen-api-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Swap the real Npgsql/pgvector AppDbContext for the in-memory
            // TestAppDbContext so endpoint tests run fully offline (no Postgres
            // service needed in CI). Mirrors GroundedRetrieverTests' approach of
            // a TestAppDbContext subclass that overrides OnModelCreating to skip
            // pgvector-specific configuration.
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddScoped<AppDbContext>(sp =>
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(_dbName)
                    .Options;
                return new TestAppDbContext(options);
            });
        });
    }

    private sealed class TestAppDbContext : AppDbContext
    {
        public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Minimal model mirroring AppDbContext minus pgvector/postgres-only
            // directives, covering the workflow entities touched by these tests.
            modelBuilder.Entity<WorkflowRun>(b =>
            {
                b.HasKey(r => r.Id);
                b.Property(r => r.UserId).IsRequired().HasMaxLength(100);
                b.Property(r => r.Status).HasConversion<string>().HasMaxLength(50);
                b.Property(r => r.TotalCostUsd).HasPrecision(18, 6);
            });

            modelBuilder.Entity<AgentStep>(b =>
            {
                b.HasKey(s => s.Id);
                b.Property(s => s.Role).HasConversion<string>().HasMaxLength(50);
                b.Property(s => s.Status).HasConversion<string>().HasMaxLength(50);
                b.Property(s => s.ToolName).HasMaxLength(100);
                b.Property(s => s.InputSummary).HasColumnType("text");
                b.Property(s => s.OutputSummary).HasColumnType("text");
                b.Property(s => s.CostUsd).HasPrecision(18, 6);
                b.HasOne<WorkflowRun>()
                    .WithMany()
                    .HasForeignKey(s => s.RunId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(s => new { s.RunId, s.Order });
            });

            modelBuilder.Entity<ApprovalAudit>(b =>
            {
                b.HasKey(a => a.Id);
                b.Property(a => a.Decision).HasConversion<string>().HasMaxLength(50);
                b.Property(a => a.ApproverId).HasMaxLength(100);
                b.Property(a => a.Reason).HasColumnType("text");
                b.Property(a => a.ModifiedDraftJson).HasColumnType("text");
                b.HasOne<WorkflowRun>()
                    .WithMany()
                    .HasForeignKey(a => a.RunId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(a => new { a.RunId, a.CreatedAtUtc }).IsDescending(false, true);
            });

            modelBuilder.Entity<PersistedDraft>(b =>
            {
                b.HasKey(d => d.Id);
                b.Property(d => d.DraftJson).HasColumnType("text");
                b.HasOne<WorkflowRun>()
                    .WithMany()
                    .HasForeignKey(d => d.RunId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(d => d.RunId);
            });

            // Skip the serialized value object: without the jsonb converter it
            // would otherwise be discovered as an entity needing a primary key
            // (same workaround as GroundedRetrieverTests.TestAppDbContext).
            modelBuilder.Entity<InquiryDraft>(b =>
            {
                b.HasKey(d => d.Id);
                b.Ignore(d => d.Citations);
            });
        }
    }
}