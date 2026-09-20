using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CitizenServicesCopilot.Api.DTOs;
using CitizenServicesCopilot.Api.Streaming;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Orchestration;
using CitizenServicesCopilot.Domain.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static IServiceCollection AddWorkflowServices(this IServiceCollection services)
    {
        // WorkflowOrchestrator is not registered by AddApplicationServices. Register
        // it here so endpoints can resolve it.
        services.AddScoped<WorkflowOrchestrator>();
        services.AddScoped<OrchestratorOptions>(sp =>
        {
            var options = new OrchestratorOptions();
            sp.GetRequiredService<IConfiguration>()
                .GetSection(OrchestratorOptions.SectionName)
                .Bind(options);
            return options;
        });
        return services;
    }

    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/workflows/citizen-response
        app.MapPost("/api/workflows/citizen-response", async (
            SubmitWorkflowRequest request,
            IServiceProvider services,
            IConfiguration config,
            ICorrelationContext requestCorrelation,
            ILogger<WorkflowOrchestrator> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest(new { message = "UserId and Question are required." });
            }

            var runId = Guid.NewGuid();
            var modelName = config["LlmSettings:DefaultModel"] ?? "gpt-4o-mini";

            // Correlate the background run with the originating request so the
            // orchestrator and every recorded step share the same correlation id.
            var correlationId = requestCorrelation.CorrelationId;

            // TODO-D: Replace fire-and-forget with a durable queue (Checkpoint D).
            // Current implementation risks task loss on app restart. Documented in
            // SDD Part B gap table.
            _ = Task.Run(async () =>
            {
                try
                {
                    // Resolve in a dedicated scope: the request scope (and its
                    // scoped EF DbContext) is disposed when the handler returns
                    // its 202, which would break any long-running background work.
                    using var scope = services.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ICorrelationContext>().CorrelationId = correlationId;
                    var orchestrator = scope.ServiceProvider.GetRequiredService<WorkflowOrchestrator>();
                    await orchestrator.RunAsync(request.UserId, request.Question, modelName, CancellationToken.None, runId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Workflow {RunId} failed in background task", runId);
                }
            });

            return Results.Accepted($"/api/runs/{runId}", new { runId });
        })
        .WithName("SubmitCitizenResponse")
        .WithTags("Workflows");

        // GET /api/runs/{runId}
        app.MapGet("/api/runs/{runId:guid}", async (
            Guid runId,
            HttpContext http,
            IWorkflowRunRepository runRepo,
            IAgentStepRepository stepRepo,
            CancellationToken ct) =>
        {
            var run = await runRepo.GetByIdAsync(runId, ct);
            if (run is null) return Results.NotFound(new { message = $"Run {runId} not found." });

            // Ownership: citizens may only read their own runs; officers may
            // read any run.
            if (!IsOfficer(http) && !string.Equals(run.UserId, GetUserId(http), StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var steps = await stepRepo.GetForRunAsync(runId, ct);

            var response = new WorkflowRunResponse(
                RunId: run.Id,
                Status: run.Status.ToString(),
                StartedAtUtc: run.StartedAtUtc,
                CompletedAtUtc: run.CompletedAtUtc,
                TotalCostUsd: run.TotalCostUsd,
                TotalTokensIn: steps.Sum(s => s.TokensIn ?? 0),
                TotalTokensOut: steps.Sum(s => s.TokensOut ?? 0),
                ErrorMessage: run.ErrorMessage,
                Steps: steps.OrderBy(s => s.Order).Select(s => new StepResponse(
                    Order: s.Order,
                    Role: s.Role.ToString(),
                    Status: s.Status.ToString(),
                    ToolName: s.ToolName,
                    TokensIn: s.TokensIn,
                    TokensOut: s.TokensOut,
                    CostUsd: s.CostUsd,
                    DurationMs: s.DurationMs,
                    OutputSummary: s.OutputSummary,
                    ErrorMessage: s.ErrorMessage)).ToList());

            return Results.Ok(response);
        })
        .WithName("GetWorkflowRun")
        .WithTags("Workflows")
        .RequireAuthorization();

        // POST /api/runs/{runId}/approve
        app.MapPost("/api/runs/{runId}/approve", async (
            Guid runId,
            ApprovalRequest request,
            IApprovalService approval,
            CancellationToken ct) =>
        {
            try
            {
                var audit = await approval.ApproveAsync(runId.ToString(), request.ApproverId, ct);
                return Results.Ok(ToApprovalResponse(audit));
            }
            catch (ApprovalAlreadyDecidedException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (EntityNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        })
        .WithName("ApproveWorkflowRun")
        .WithTags("Workflows")
        .RequireAuthorization("Officer");

        // POST /api/runs/{runId}/reject
        app.MapPost("/api/runs/{runId}/reject", async (
            Guid runId,
            RejectRequest request,
            IApprovalService approval,
            CancellationToken ct) =>
        {
            try
            {
                var audit = await approval.RejectAsync(runId.ToString(), request.ApproverId, request.Reason, ct);
                return Results.Ok(ToApprovalResponse(audit));
            }
            catch (ApprovalAlreadyDecidedException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (EntityNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        })
        .WithName("RejectWorkflowRun")
        .WithTags("Workflows")
        .RequireAuthorization("Officer");

        // POST /api/runs/{runId}/edit-and-approve
        app.MapPost("/api/runs/{runId}/edit-and-approve", async (
            Guid runId,
            EditAndApproveRequest request,
            IApprovalService approval,
            CancellationToken ct) =>
        {
            try
            {
                var audit = await approval.EditAndApproveAsync(
                    runId.ToString(), request.ApproverId, request.EditedDraftJson, request.Reason, ct);
                return Results.Ok(ToApprovalResponse(audit));
            }
            catch (InvalidEditedContentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (ApprovalAlreadyDecidedException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (EntityNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        })
        .WithName("EditAndApproveWorkflowRun")
        .WithTags("Workflows")
        .RequireAuthorization("Officer");

        // GET /api/users/{userId}/spend
        app.MapGet("/api/users/{userId}/spend", async (
            string userId,
            HttpContext http,
            IUserBudgetRepository budgetRepo,
            IWorkflowRunRepository runRepo,
            IAgentStepRepository stepRepo,
            CancellationToken ct) =>
        {
            // Ownership: citizens may only view their own spend; officers may
            // view any user's spend.
            if (!IsOfficer(http) && !string.Equals(userId, GetUserId(http), StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var budget = await budgetRepo.GetByUserIdAsync(userId, ct);
            if (budget is null)
            {
                return Results.NotFound(new { message = $"No budget record for user '{userId}'." });
            }

            // Aggregated token/cost accounting from the user's persisted steps.
            var tokensIn = 0;
            var tokensOut = 0;
            var costUsd = 0m;
            var runs = await runRepo.GetByUserIdAsync(userId, ct);
            foreach (var run in runs)
            {
                var steps = await stepRepo.GetForRunAsync(run.Id, ct);
                tokensIn += steps.Sum(s => s.TokensIn ?? 0);
                tokensOut += steps.Sum(s => s.TokensOut ?? 0);
                costUsd += steps.Sum(s => s.CostUsd);
            }

            var now = DateTimeOffset.UtcNow;
            var periodStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, DateTimeOffset.UtcNow.Offset);
            var periodEnd = periodStart.AddMonths(1);

            return Results.Ok(new SpendViewResponse(
                UserId: userId,
                TokensIn: tokensIn,
                TokensOut: tokensOut,
                CostUsd: costUsd,
                BudgetLimitUsd: budget.AllocatedBudgetUsd,
                BudgetRemainingUsd: budget.AllocatedBudgetUsd - budget.SpentUsd,
                PeriodStartUtc: periodStart,
                PeriodEndUtc: periodEnd));
        })
        .WithName("GetUserSpend")
        .WithTags("Workflows")
        .RequireAuthorization();

        // GET /api/workflows/stream?userId={userId}&question={question}
        // Server-Sent Events: streams "stage"/"step"/"done"/"error" progress events
        // for a run executed on the request path. A client disconnect aborts the
        // run (requestAborted propagates into the orchestrator).
        app.MapGet("/api/workflows/stream", async (
            string userId,
            string question,
            IServiceProvider services,
            IConfiguration config,
            ICorrelationContext requestCorrelation,
            HttpContext http,
            ILogger<WorkflowOrchestrator> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(question))
            {
                return Results.BadRequest(new { message = "UserId and Question are required." });
            }

            var response = http.Response;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";
            await response.StartAsync(ct);

            var modelName = config["LlmSettings:DefaultModel"] ?? "gpt-4o-mini";

            // Unbounded channel: events are few and small; writers must never
            // deadlock the orchestrator behind a slow or disconnected client.
            var channel = Channel.CreateUnbounded<WorkflowEvent>(new UnboundedChannelOptions { SingleReader = true });
            var sink = new ChannelWorkflowProgressSink(channel);

            var runTask = Task.Run(async () =>
            {
                try
                {
                    // Dedicated scope mirrors the background path so the scoped
                    // DbContext outlives this request handler's stream pump.
                    using var scope = services.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ICorrelationContext>().CorrelationId = requestCorrelation.CorrelationId;
                    var orchestrator = scope.ServiceProvider.GetRequiredService<WorkflowOrchestrator>();
                    await orchestrator.RunAsync(userId, question, modelName, ct, progress: sink);
                }
                finally
                {
                    sink.Complete();
                }
            }, CancellationToken.None);

            try
            {
                await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                {
                    var payload = JsonSerializer.Serialize(evt, JsonSerializerOptions.Web);
                    await response.WriteAsync("data: " + payload + "\n\n", Encoding.UTF8, ct);
                    await response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client disconnected; the run aborts through the same token.
            }
            finally
            {
                // Observe run task failures so they surface as logged errors
                // rather than unobserved-task-exceptions.
                try
                {
                    await runTask;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Workflow stream run {UserId} failed after disconnect", userId);
                }
            }

            return Results.Empty;
        })
        .WithName("StreamWorkflowProgress")
        .WithTags("Workflows");

        return app;
    }

    private static bool IsOfficer(HttpContext http) =>
        http.User.IsInRole("Officer");

    private static string? GetUserId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static ApprovalResponse ToApprovalResponse(ApprovalAudit a) => new(
        Id: a.Id,
        RunId: a.RunId,
        Decision: a.Decision.ToString(),
        ApproverId: a.ApproverId,
        Reason: a.Reason,
        ModifiedDraftJson: a.ModifiedDraftJson,
        CreatedAtUtc: a.CreatedAtUtc);
}