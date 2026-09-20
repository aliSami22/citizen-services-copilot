using System;
using System.Collections.Generic;

namespace CitizenServicesCopilot.Api.DTOs;

public record SubmitWorkflowRequest(string UserId, string Question);

public record WorkflowRunResponse(
    Guid RunId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    decimal TotalCostUsd,
    string? ErrorMessage,
    IReadOnlyList<StepResponse> Steps);

public record StepResponse(
    int Order,
    string Role,
    string Status,
    string? ToolName,
    int? TokensIn,
    int? TokensOut,
    decimal CostUsd,
    int? DurationMs,
    string? OutputSummary,
    string? ErrorMessage);

public record ApprovalRequest(string ApproverId);

public record RejectRequest(string ApproverId, string Reason);

public record EditAndApproveRequest(string ApproverId, string EditedDraftJson, string? Reason);

public record ApprovalResponse(
    Guid Id,
    Guid RunId,
    string Decision,
    string? ApproverId,
    string? Reason,
    string? ModifiedDraftJson,
    DateTimeOffset CreatedAtUtc);

public record SpendViewResponse(
    string UserId,
    int TokensIn,
    int TokensOut,
    decimal CostUsd,
    decimal BudgetLimitUsd,
    decimal BudgetRemainingUsd,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc);