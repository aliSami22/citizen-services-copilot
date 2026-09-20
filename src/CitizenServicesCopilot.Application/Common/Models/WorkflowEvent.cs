namespace CitizenServicesCopilot.Application.Common.Models;

/// <summary>
/// A single progress event emitted by the workflow orchestrator while a run
/// is in flight. Consumed by real-time consumers (e.g. an SSE stream).
/// </summary>
/// <param name="Type">Event kind: "stage" | "step" | "done" | "error".</param>
/// <param name="RunId">The run the event belongs to.</param>
/// <param name="Stage">Stage name for "stage"/"step" events (agent role, tool name).</param>
/// <param name="Status">Step status for "step", terminal run status for "done".</param>
/// <param name="Order">Persisted step order for "step" events.</param>
/// <param name="Message">Human-readable detail (error message, output summary).</param>
public sealed record WorkflowEvent(
    string Type,
    Guid RunId,
    string? Stage = null,
    string? Status = null,
    int? Order = null,
    string? Message = null);

public static class WorkflowEventTypes
{
    public const string Stage = "stage";
    public const string Step = "step";
    public const string Done = "done";
    public const string Error = "error";
}