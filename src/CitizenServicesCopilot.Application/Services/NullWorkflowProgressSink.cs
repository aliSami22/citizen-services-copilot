using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services;

/// <summary>
/// Default progress sink used when no consumer subscribes. Keeps background
/// runs (fire-and-forget citizen-response) free of stream-plumbing overhead.
/// The orchestrator treats publishing as best-effort, so this is a no-op.
/// </summary>
public sealed class NullWorkflowProgressSink : IWorkflowProgressSink
{
    /// <summary>Shared singleton; the sink is stateless.</summary>
    public static readonly NullWorkflowProgressSink Instance = new();

    public Task PublishAsync(WorkflowEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}