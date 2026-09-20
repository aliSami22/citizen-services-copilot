namespace CitizenServicesCopilot.Application.Common.Models;

/// <summary>
/// Tuning knobs for the multi-agent workflow orchestrator.
/// </summary>
public sealed class OrchestratorOptions
{
    public const string SectionName = "Orchestrator";

    /// <summary>
    /// Hard cap on agent attempts across a single run (attempts include retries).
    /// Guards against unbounded loops; trip results in a Failed terminal state.
    /// </summary>
    public int MaxIterations { get; set; } = 6;

    /// <summary>
    /// Per-attempt timeout applied to every agent execution.
    /// </summary>
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of additional attempts after the first.
    /// </summary>
    public int RetryAttempts { get; set; } = 2;

    /// <summary>
    /// Exponential backoff base (milliseconds): delay = Base * Factor^(attempt - 1).
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    public int RetryBackoffFactor { get; set; } = 2;

    /// <summary>
    /// When true, a failed agent chain falls back to plain RAG: retrieve once
    /// and draft once, marking the degraded stage as <c>Degraded</c>.
    /// </summary>
    public bool EnableGracefulDegradation { get; set; } = true;

    /// <summary>
    /// Poll interval while waiting for a human approval record.
    /// </summary>
    public TimeSpan ApprovalPollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Hard cap on how long the orchestrator waits for a human approval
    /// decision; on expiry the run is marked Failed with "approval timeout".
    /// </summary>
    public TimeSpan ApprovalWaitTimeout { get; set; } = TimeSpan.FromMinutes(5);
}