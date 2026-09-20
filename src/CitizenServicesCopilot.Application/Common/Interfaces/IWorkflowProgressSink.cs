using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Push-style sink for workflow progress events. The orchestrator publishes
/// stage/step/done/error events as a run progresses; consumers (SSE streams,
/// durable queues, log aggregation) subscribe by implementing this contract.
/// </summary>
public interface IWorkflowProgressSink
{
    /// <summary>
    /// Persists or forwards a single progress event. Best-effort: the
    /// orchestrator must never fail the run because publishing failed.
    /// </summary>
    Task PublishAsync(WorkflowEvent evt, CancellationToken ct = default);
}