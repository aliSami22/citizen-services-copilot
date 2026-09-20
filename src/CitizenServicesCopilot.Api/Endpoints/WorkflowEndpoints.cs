using CitizenServicesCopilot.Api.DTOs;
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
    private const string OfficerRole = "Officer";

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
            ILogger<WorkflowOrchestrator> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest(new { message = "UserId and Question are required." });
            }

            var runId = Guid.NewGuid();
            var modelName = config["LlmSettings:DefaultModel"] ?? "gpt-4o-mini";

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
            IWorkflowRunRepository runRepo,
            IAgentStepRepository stepRepo,
            CancellationToken ct) =>
        {
            var run = await runRepo.GetByIdAsync(runId, ct);
            if (run is null) return Results.NotFound(new { message = $"Run {runId} not found." });

            var steps = await stepRepo.GetForRunAsync(runId, ct);

            var response = new WorkflowRunResponse(
                RunId: run.Id,
                Status: run.Status.ToString(),
                StartedAtUtc: run.StartedAtUtc,
                CompletedAtUtc: run.CompletedAtUtc,
                TotalCostUsd: run.TotalCostUsd,
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
        .WithTags("Workflows");

        // POST /api/runs/{runId}/approve
        app.MapPost("/api/runs/{runId}/approve", async (
            Guid runId,
            ApprovalRequest request,
            HttpContext http,
            IApprovalService approval,
            CancellationToken ct) =>
        {
            if (!IsOfficer(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

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
        .WithTags("Workflows");

        // POST /api/runs/{runId}/reject
        app.MapPost("/api/runs/{runId}/reject", async (
            Guid runId,
            RejectRequest request,
            HttpContext http,
            IApprovalService approval,
            CancellationToken ct) =>
        {
            if (!IsOfficer(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

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
        .WithTags("Workflows");

        // POST /api/runs/{runId}/edit-and-approve
        app.MapPost("/api/runs/{runId}/edit-and-approve", async (
            Guid runId,
            EditAndApproveRequest request,
            HttpContext http,
            IApprovalService approval,
            CancellationToken ct) =>
        {
            if (!IsOfficer(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);

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
        .WithTags("Workflows");

        return app;
    }

    private static bool IsOfficer(HttpContext http) =>
        http.Request.Headers.TryGetValue("X-Role", out var role) &&
        string.Equals(role.ToString(), OfficerRole, StringComparison.OrdinalIgnoreCase);

    private static ApprovalResponse ToApprovalResponse(ApprovalAudit a) => new(
        Id: a.Id,
        RunId: a.RunId,
        Decision: a.Decision.ToString(),
        ApproverId: a.ApproverId,
        Reason: a.Reason,
        ModifiedDraftJson: a.ModifiedDraftJson,
        CreatedAtUtc: a.CreatedAtUtc);
}