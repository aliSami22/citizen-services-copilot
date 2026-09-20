using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CitizenServicesCopilot.Domain.Agents;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Workflows;
using CitizenServicesCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CitizenServicesCopilot.Api.Tests;

public class WorkflowEndpointTests : IClassFixture<WorkflowApiFactory>
{
    private readonly HttpClient _client;
    private readonly WorkflowApiFactory _factory;

    public WorkflowEndpointTests(WorkflowApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_CitizenResponse_Returns202WithRunId()
    {
        // Identity comes from the JWT "sub" claim, so the submit must be
        // authenticated and the body carries no userId.
        var citizen = await _factory.CreateAuthenticatedClientAsync("u-1", "Citizen");
        var resp = await citizen.PostAsJsonAsync("/api/workflows/citizen-response",
            new { question = "What is the unemployment benefit?" });
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
            var runResp = await citizen.GetAsync($"/api/runs/{runId}");
            status = runResp.StatusCode;
            if (status == HttpStatusCode.OK)
            {
                break;
            }
        }

        Assert.Equal(HttpStatusCode.OK, status);

        // Await the background run's terminal state so its fire-and-forget task
        // does not leak into sibling tests (empty corpus -> retrieval refusal ->
        // Failed). Bounded wait keeps this from blocking on a broken run.
        var terminal = await WaitForTerminalAsync(runId, citizen);
        Assert.Equal("Failed", terminal);
    }

    [Fact]
    public async Task Post_CitizenResponse_WithoutToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/workflows/citizen-response",
            new { question = "What is the unemployment benefit?" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Post_CitizenResponse_EmptyQuestion_Returns400()
    {
        var citizen = await _factory.CreateAuthenticatedClientAsync("u-1", "Citizen");
        var resp = await citizen.PostAsJsonAsync("/api/workflows/citizen-response",
            new { question = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Run_NotFound_Returns404()
    {
        var citizen = await _factory.CreateAuthenticatedClientAsync("u-reader", "Citizen");
        var resp = await citizen.GetAsync($"/api/runs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private async Task<string> WaitForTerminalAsync(string runId, HttpClient client)
    {
        string status = "Created";
        for (var i = 0; i < 40; i++)
        {
            var resp = await client.GetAsync($"/api/runs/{runId}");
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var body = await resp.Content.ReadAsStringAsync();
                status = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("status").GetString() ?? status;
            }
            resp.Dispose();
            if (status is "Approved" or "Rejected" or "Failed" or "Cancelled")
            {
                return status;
            }
            await Task.Delay(250);
        }
        return status;
    }

    [Fact]
    public async Task Approve_WithoutToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/approve", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Reject_WithoutToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/reject",
            new { reason = "insufficient evidence" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Approve_AsCitizen_Returns403()
    {
        var citizen = await _factory.CreateAuthenticatedClientAsync("u-civ", "Citizen");
        var resp = await citizen.PostAsJsonAsync($"/api/runs/{Guid.NewGuid()}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task EditAndApprove_InvalidJson_Returns400_WhenOfficer()
    {
        var officer = await _factory.CreateAuthenticatedClientAsync("o-1", "Officer");
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/runs/{Guid.NewGuid()}/edit-and-approve")
        {
            Content = JsonContent.Create(new
            {
                editedDraftJson = "not valid json",
                reason = (string?)null
            })
        };
        var resp = await officer.SendAsync(req);
        // Acceptable: 400 (invalid JSON), 404 (run not found), 409 (already decided).
        // The test's intent is: it does NOT 200, and it does NOT 403.
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Approve_OnUnknownRun_WithOfficer_DoesNotReturn403()
    {
        var officer = await _factory.CreateAuthenticatedClientAsync("o-2", "Officer");
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/runs/{Guid.NewGuid()}/approve")
        {
            Content = JsonContent.Create(new { })
        };
        var resp = await officer.SendAsync(req);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Approve_RecordsApproverFromJwt_NotBody()
    {
        // The run's owner is irrelevant here; identity is asserted from the
        // JWT "sub" claim, so the body must NOT be able to influence it.
        var officerId = $"o-appr-{Guid.NewGuid():N}";
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = WorkflowRun.Create($"u-owner-{Guid.NewGuid():N}");
            db.WorkflowRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        // Deliberately send a forged approverId in the body; it must be ignored.
        var officer = await _factory.CreateAuthenticatedClientAsync(officerId, "Officer");
        var resp = await officer.PostAsJsonAsync($"/api/runs/{runId}/approve",
            new { approverId = "spoofed-approver" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(officerId, body.GetProperty("approverId").GetString());
        Assert.Equal("Approved", body.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Get_Spend_NoBudgetRecord_Returns404()
    {
        var officer = await _factory.CreateAuthenticatedClientAsync("o-3", "Officer");
        var resp = await officer.GetAsync($"/api/users/{Guid.NewGuid():N}/spend");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Spend_AsDifferentCitizen_Returns403()
    {
        var citizen = await _factory.CreateAuthenticatedClientAsync("u-other", "Citizen");
        var resp = await citizen.GetAsync($"/api/users/u-owner/spend");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Spend_AfterRun_ReturnsAggregatedValues()
    {
        var userId = $"u-spend-{Guid.NewGuid():N}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserBudgets.Add(new UserBudget
            {
                UserId = userId,
                AllocatedBudgetUsd = 10m,
                SpentUsd = 0m,
                TotalTokensUsed = 0,
                IsBlocked = false
            });

            var run = WorkflowRun.Create(userId);
            db.WorkflowRuns.Add(run);
            await db.SaveChangesAsync();

            db.AgentSteps.AddRange(
                new AgentStep(
                    Role: AgentRole.EligibilityIdentifier,
                    Status: AgentStepStatus.Succeeded,
                    CreatedAtUtc: DateTimeOffset.UtcNow,
                    OutputSummary: "eligible",
                    TokensIn: 100,
                    TokensOut: 50,
                    CostUsd: 0.001m,
                    RunId: run.Id,
                    Order: 0),
                new AgentStep(
                    Role: AgentRole.ResponseDrafter,
                    Status: AgentStepStatus.Succeeded,
                    CreatedAtUtc: DateTimeOffset.UtcNow,
                    OutputSummary: "draft",
                    TokensIn: 200,
                    TokensOut: 80,
                    CostUsd: 0.002m,
                    RunId: run.Id,
                    Order: 1));
            await db.SaveChangesAsync();
        }

        var citizen = await _factory.CreateAuthenticatedClientAsync(userId, "Citizen");
        var resp = await citizen.GetAsync($"/api/users/{userId}/spend");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId, body.GetProperty("userId").GetString());
        Assert.Equal(300, body.GetProperty("tokensIn").GetInt32());
        Assert.Equal(130, body.GetProperty("tokensOut").GetInt32());
        Assert.Equal(0.003m, body.GetProperty("costUsd").GetDecimal());
        Assert.Equal(10m, body.GetProperty("budgetLimitUsd").GetDecimal());
        Assert.Equal(10m, body.GetProperty("budgetRemainingUsd").GetDecimal());
        Assert.True(body.TryGetProperty("periodStartUtc", out _));
        Assert.True(body.TryGetProperty("periodEndUtc", out _));
    }

    [Fact]
    public async Task Login_ReturnsToken_200()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { userId = "u-1", role = "Citizen" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token!.Split('.').Length);
        Assert.True(body.TryGetProperty("expiresAtUtc", out _));
    }

    [Fact]
    public async Task Login_InvalidRole_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { userId = "u-1", role = "Admin" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Run_AsDifferentCitizen_Returns403()
    {
        var owner = $"u-owner-{Guid.NewGuid():N}";
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = WorkflowRun.Create(owner);
            db.WorkflowRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var stranger = await _factory.CreateAuthenticatedClientAsync($"u-stranger-{Guid.NewGuid():N}", "Citizen");
        var resp = await stranger.GetAsync($"/api/runs/{runId}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Run_AsOfficer_Returns200()
    {
        var owner = $"u-own-{Guid.NewGuid():N}";
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = WorkflowRun.Create(owner);
            db.WorkflowRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var officer = await _factory.CreateAuthenticatedClientAsync("o-4", "Officer");
        var resp = await officer.GetAsync($"/api/runs/{runId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}

public class WorkflowApiFactory : WebApplicationFactory<Program>
{
    // One InMemory store per factory instance; recreating it per-request-scope
    // would give every HttpContext a different (empty) database.
    private static readonly string _dbName = $"citizen-api-tests-{Guid.NewGuid():N}";

    // Test-only signing key shared by every host so issued tokens validate.
    private const string TestJwtKey = "test-only-jwt-key-0123456789abcdef0123456789abcdef";

    public async Task<string> GetTokenAsync(string userId, string role)
    {
        using var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { userId, role });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string userId, string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetTokenAsync(userId, role));
        return client;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = "CitizenServicesCopilot",
                ["Jwt:Audience"] = "CitizenServicesCopilot.Api"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Capture the CorrelationId that loggers see in BeginScope so the
            // D4 diagnostics test can assert end-to-end correlation propagation.
            var capture = new ScopeCapturingLoggerProvider();
            services.AddSingleton(capture);
            services.AddLogging(logging => logging.AddProvider(capture));

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