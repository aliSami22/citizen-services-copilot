using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Tools;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Workflows;
using Microsoft.Extensions.Logging;
using AgentRole = CitizenServicesCopilot.Domain.Agents.AgentRole;

namespace CitizenServicesCopilot.Application.Orchestration;

/// <summary>
/// Explicit state-machine orchestrator for the multi-agent workflow:
/// Retrieve -> [Eligibility -> Procedure -> Draft] -> AwaitApproval -> Persist.
///
/// Why a state machine: deterministic ordering and terminal states (Approved /
/// Rejected / Failed / Cancelled), easy audit replay from persisted steps, and a
/// hard breaker against unbounded loops.
/// </summary>
public class WorkflowOrchestrator
{
    public const string PersistDraftToolName = ToolCatalog.PersistDraft;

    /// <summary>
    /// Terminal failure reason recorded when the budget gate blocks a run.
    /// </summary>
    public const string BudgetExceededReason = "budget exceeded";

    private const string DegradationNote = "graceful degradation to plain RAG after agent chain failure";
    private const string InsufficientEvidence = "insufficient evidence";

    private readonly IReadOnlyDictionary<AgentRole, IAgent> _agents;
    private readonly IRetrievalService _retrievalService;
    private readonly IWorkflowRunRepository _runs;
    private readonly IAgentStepRepository _steps;
    private readonly IApprovalService _approvalService;
    private readonly IToolExecutor _toolExecutor;
    private readonly IToolRegistry _toolRegistry;
    private readonly IBudgetPreFlightCheck _budgetCheck;
    private readonly OrchestratorOptions _options;
    private readonly ILogger<WorkflowOrchestrator> _logger;

    private int _stageAttemptCount;
    private int _stepOrder;

    public WorkflowOrchestrator(
        IEnumerable<IAgent> agents,
        IRetrievalService retrievalService,
        IWorkflowRunRepository runs,
        IAgentStepRepository steps,
        IApprovalService approvalService,
        IToolExecutor toolExecutor,
        IToolRegistry toolRegistry,
        IBudgetPreFlightCheck budgetCheck,
        OrchestratorOptions options,
        ILogger<WorkflowOrchestrator> logger)
    {
        _agents = (agents ?? throw new ArgumentNullException(nameof(agents)))
            .ToDictionary(agent => agent.Role);
        _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _approvalService = approvalService ?? throw new ArgumentNullException(nameof(approvalService));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _budgetCheck = budgetCheck ?? throw new ArgumentNullException(nameof(budgetCheck));
        _options = options ?? new OrchestratorOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Fail fast: an agent may only declare tools that are actually registered.
        foreach (var agent in _agents.Values)
        {
            foreach (var toolName in agent.AllowedTools)
            {
                if (!_toolRegistry.Contains(toolName))
                {
                    throw new InvalidOperationException(
                        $"Agent '{agent.Role}' declares allowed tool '{toolName}' which is not registered.");
                }
            }
        }
    }

    public async Task<WorkflowRun> RunAsync(string userId, string query, string modelName, CancellationToken ct = default, Guid? runId = null)
    {
        ct.ThrowIfCancellationRequested();

        var run = WorkflowRun.Create(userId, runId);
        await _runs.AddAsync(run, ct);
        run = run with { Status = RunStatus.Running };
        await _runs.UpdateAsync(run, ct);

        _stageAttemptCount = 0;
        _stepOrder = 0;
        var recordedSteps = new List<AgentStep>();
        var input = new AgentInput(run.Id, userId, query, modelName, Array.Empty<DocumentChunk>(), recordedSteps);

        try
        {
            // Stage 0 - grounded retrieval. §3 R-2: refuse when there is no evidence to ground a response.
            var retrieval = await _retrievalService.RetrieveAsync(new RetrievalQuery(query), ct);
            if (retrieval.IsRefusal || retrieval.Chunks.Count == 0)
            {
                return await FailAsync(run, retrieval.RefusalReason ?? InsufficientEvidence, ct);
            }

            input = input with { ContextChunks = retrieval.Chunks.Select(c => c.Chunk).ToList() };

            // Budget gate: evaluated before the first agent and between stages.
            // Denied/WouldExceed terminates the run with a hard cut-off.
            var budgetFail = await EnforceBudgetAsync(run, query, ct);
            if (budgetFail is not null)
            {
                return budgetFail;
            }

            AgentStep draftStep;
            try
            {
                var eligibilityStep = await RunStageAsync(AgentRole.EligibilityIdentifier, input, ct);
                recordedSteps.Add(eligibilityStep);

                budgetFail = await EnforceBudgetAsync(run, query, ct);
                if (budgetFail is not null)
                {
                    return budgetFail;
                }

                var procedureStep = await RunStageAsync(AgentRole.ProcedureResolver, input, ct);
                recordedSteps.Add(procedureStep);

                budgetFail = await EnforceBudgetAsync(run, query, ct);
                if (budgetFail is not null)
                {
                    return budgetFail;
                }

                draftStep = await RunStageAsync(AgentRole.ResponseDrafter, input, ct);
                recordedSteps.Add(draftStep);
            }
            catch (AgentStageFailureException failure)
            {
                if (!_options.EnableGracefulDegradation)
                {
                    return await FailAsync(run, $"agent chain failed on stage '{failure.Role}': {failure.Error}", ct);
                }

                _logger.LogWarning(
                    "Agent stage {Role} failed after retries; degrading to plain RAG. {Error}",
                    failure.Role,
                    failure.Error);

                var degraded = await DegradeAsync(run, input, failure.Role, recordedSteps, ct);
                if (degraded.Finished)
                {
                    return degraded.Run;
                }

                draftStep = degraded.DraftStep!;
            }

            return await CompleteAfterDraftAsync(run, draftStep, recordedSteps, ct);
        }
        catch (MaxIterationsExceededException)
        {
            return await FailAsync(run, $"max iterations ({_options.MaxIterations}) reached", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await FinishAsync(run, RunStatus.Cancelled, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Waits for a human approval record, then persists the draft — only for
    /// Approved / EditedAndApproved decisions. Rejected terminates the run.
    /// </summary>
    private async Task<WorkflowRun> CompleteAfterDraftAsync(
        WorkflowRun run, AgentStep draftStep, List<AgentStep> recordedSteps, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(draftStep.OutputSummary))
        {
            return await FailAsync(run, "draft produced no output", ct);
        }

        var (isRefusal, refusalReason) = ReadDraftRefusal(draftStep.OutputSummary);
        if (isRefusal)
        {
            return await FailAsync(run, refusalReason ?? "draft refused", ct);
        }

        run = run with { Status = RunStatus.WaitingApproval };
        await _runs.UpdateAsync(run, ct);

        // Poll the single source of truth (IApprovalService) until a decision
        // arrives or the approval wait budget expires.
        var approval = await WaitForApprovalAsync(run, ct);
        if (approval is null)
        {
            return await FailAsync(run, "approval timeout", ct);
        }

        if (approval.Decision == ApprovalDecision.Rejected)
        {
            return await FinishAsync(run, RunStatus.Rejected, ct);
        }

        if (approval.Decision == ApprovalDecision.Pending)
        {
            return await FailAsync(run, "unexpected pending approval record", ct);
        }

        // Approved or EditedAndApproved -> the draft may be persisted.
        // §3 R-5: an edit-and-approve decision persists the edited draft.
        var draftToPersist = approval.ModifiedDraftJson ?? draftStep.OutputSummary;
        var payload = JsonSerializer.SerializeToElement(new
        {
            runId = run.Id.ToString(),
            draftJson = draftToPersist
        });

        var persistStep = await RunPersistStageAsync(run.Id, payload, ct);
        if (persistStep.Status != AgentStepStatus.Succeeded)
        {
            return await FailAsync(run, $"persist failed: {persistStep.ErrorMessage}", ct);
        }

        recordedSteps.Add(persistStep);

        return await FinishAsync(run, RunStatus.Approved, ct);
    }

    /// <summary>
    /// Polls for a human approval decision until one is recorded or the
    /// <see cref="OrchestratorOptions.ApprovalWaitTimeout"/> budget is consumed.
    /// Returns null when the wait timed out.
    /// </summary>
    private async Task<ApprovalAudit?> WaitForApprovalAsync(WorkflowRun run, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _options.ApprovalWaitTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var record = await _approvalService.GetForRunAsync(run.Id.ToString(), ct);
            if (record is not null)
            {
                return record;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(_options.ApprovalPollingInterval, ct);
        }
    }

    /// <summary>
    /// Fallback for a failed agent chain: retrieve once, draft once, and mark
    /// the degraded stage. Returns <see cref="DegradedOutcome.Finished"/>=true
    /// when the run has already terminated (refusal or failure).
    /// </summary>
    private async Task<DegradedOutcome> DegradeAsync(
        WorkflowRun run, AgentInput input, AgentRole failedRole, List<AgentStep> recordedSteps, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var degradedStep = new AgentStep(
            Role: failedRole,
            Status: AgentStepStatus.Degraded,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            OutputSummary: DegradationNote);
        degradedStep = PrepareForPersistence(degradedStep, run.Id);
        recordedSteps.Add(degradedStep);
        await _steps.AddAsync(degradedStep, ct);

        // Degradation re-runs retrieval so the fallback re-applies the evidence gate.
        var retrieval = await _retrievalService.RetrieveAsync(new RetrievalQuery(input.Query), ct);
        if (retrieval.IsRefusal || retrieval.Chunks.Count == 0)
        {
            var finished = await FailAsync(run, retrieval.RefusalReason ?? InsufficientEvidence, ct);
            return new DegradedOutcome(true, finished, null, recordedSteps);
        }

        var degradedInput = input with
        {
            ContextChunks = retrieval.Chunks.Select(c => c.Chunk).ToList(),
            PriorSteps = recordedSteps
        };

        AgentStep draftStep;
        try
        {
            draftStep = await RunStageAsync(AgentRole.ResponseDrafter, degradedInput, ct);
        }
        catch (Exception ex) when (ex is AgentStageFailureException or MaxIterationsExceededException)
        {
            var finished = await FailAsync(run, $"degraded draft failed: {ex.Message}", ct);
            return new DegradedOutcome(true, finished, null, recordedSteps);
        }

        recordedSteps.Add(draftStep);

        return new DegradedOutcome(false, run, draftStep, recordedSteps);
    }

    /// <summary>
    /// Runs a single agent stage with per-attempt timeout, exponential-backoff
    /// retries, and the global max-iteration breaker.
    /// </summary>
    private async Task<AgentStep> RunStageAsync(AgentRole role, AgentInput input, CancellationToken ct)
    {
        if (!_agents.TryGetValue(role, out var agent))
        {
            throw new AgentStageFailureException(role, $"no agent registered for role '{role}'");
        }

        var attempt = 0;
        AgentStep? lastStep = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            _stageAttemptCount++;
            if (_stageAttemptCount > _options.MaxIterations)
            {
                throw new MaxIterationsExceededException(_options.MaxIterations);
            }

            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                lastStep = await agent
                    .ExecuteAsync(input, ct)
                    .WaitAsync(_options.StepTimeout, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                lastStep = new AgentStep(
                    Role: role,
                    Status: AgentStepStatus.Failed,
                    CreatedAtUtc: startedAt,
                    OutputSummary: null,
                    ErrorMessage: $"step timed out after {_options.StepTimeout}",
                    DurationMs: (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                lastStep = new AgentStep(
                    Role: role,
                    Status: AgentStepStatus.Failed,
                    CreatedAtUtc: startedAt,
                    OutputSummary: null,
                    ErrorMessage: ex.Message,
                    DurationMs: (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            }

            lastStep = PrepareForPersistence(lastStep, input.RunId);
            await _steps.AddAsync(lastStep, ct);

            if (lastStep.Status == AgentStepStatus.Succeeded)
            {
                return lastStep;
            }

            attempt++;
            if (attempt > _options.RetryAttempts)
            {
                throw new AgentStageFailureException(role, lastStep.ErrorMessage ?? "unknown error");
            }

            var backoffMs = _options.RetryBaseDelayMs * (int)Math.Pow(_options.RetryBackoffFactor, attempt - 1);
            _logger.LogWarning(
                "Agent stage {Role} failed on attempt {Attempt}; retrying in {Delay}ms. Error: {Error}",
                role, attempt, backoffMs, lastStep.ErrorMessage);

            await Task.Delay(backoffMs, ct);
        }
    }

    private async Task<AgentStep> RunPersistStageAsync(Guid runId, JsonElement payload, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;

        // The write-gated persist tool must exist and be flagged as a write.
        if (!_toolRegistry.TryGet(PersistDraftToolName, out var persistTool))
        {
            var missing = PrepareForPersistence(new AgentStep(
                Role: AgentRole.Persist,
                Status: AgentStepStatus.Failed,
                CreatedAtUtc: startedAt,
                OutputSummary: null,
                ErrorMessage: $"write tool '{PersistDraftToolName}' is not registered"), runId);
            await _steps.AddAsync(missing, ct);
            return missing;
        }

        if (!persistTool.IsWrite)
        {
            var notWrite = PrepareForPersistence(new AgentStep(
                Role: AgentRole.Persist,
                Status: AgentStepStatus.Failed,
                CreatedAtUtc: startedAt,
                OutputSummary: null,
                ErrorMessage: $"tool '{PersistDraftToolName}' is not flagged as a write tool"), runId);
            await _steps.AddAsync(notWrite, ct);
            return notWrite;
        }

        var result = await _toolExecutor.ExecuteAsync(PersistDraftToolName, payload, ct);

        var step = PrepareForPersistence(result.Success
            ? new AgentStep(
                Role: AgentRole.Persist,
                Status: AgentStepStatus.Succeeded,
                CreatedAtUtc: startedAt,
                OutputSummary: "draft persisted after approval",
                ErrorMessage: null)
            : new AgentStep(
                Role: AgentRole.Persist,
                Status: AgentStepStatus.Failed,
                CreatedAtUtc: startedAt,
                OutputSummary: null,
                ErrorMessage: result.Error ?? "persist_draft failed"), runId);

        await _steps.AddAsync(step, ct);
        return step;
    }

    /// <summary>
    /// Binds the persistence-framework fields (Id, RunId, Order) onto a step so
    /// it can be stored. Callers that already assigned these fields are untouched.
    /// </summary>
    private AgentStep PrepareForPersistence(AgentStep step, Guid runId) => step with
    {
        Id = step.Id == Guid.Empty ? Guid.NewGuid() : step.Id,
        RunId = runId,
        Order = _stepOrder++
    };

    /// <summary>
    /// Budget gate with a hard cut-off: Denied (hard-blocked) or WouldExceed
    /// terminates the run with <see cref="BudgetExceededReason"/>. Returns null
    /// when the run may proceed.
    /// </summary>
    private async Task<WorkflowRun?> EnforceBudgetAsync(WorkflowRun run, string query, CancellationToken ct)
    {
        var result = await _budgetCheck.CheckAsync(run.UserId, EstimateTokens(query), ct);
        if (result == BudgetCheckResult.Allowed)
        {
            return null;
        }

        return await FailAsync(run, BudgetExceededReason, ct);
    }

    /// <summary>
    /// Rough token estimate for a stage/run: ~1 token per 4 chars plus a fixed
    /// context overhead. One conservative estimate is reused for every gate.
    /// </summary>
    private static int EstimateTokens(string query) =>
        Math.Max(1000, (query.Length / 4) + 700);

    private async Task<WorkflowRun> FailAsync(WorkflowRun run, string reason, CancellationToken ct)
    {
        _logger.LogWarning("Workflow run {RunId} failed: {Reason}", run.Id, reason);
        run = run with { ErrorMessage = reason };
        return await FinishAsync(run, RunStatus.Failed, ct);
    }

    private async Task<WorkflowRun> FinishAsync(WorkflowRun run, RunStatus terminal, CancellationToken ct)
    {
        run = run with { Status = terminal, CompletedAtUtc = DateTimeOffset.UtcNow };
        await _runs.UpdateAsync(run, ct);
        return run;
    }

    private static (bool IsRefusal, string? Reason) ReadDraftRefusal(string draftJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(draftJson);
            if (doc.RootElement.TryGetProperty("isRefusal", out var isProp) && isProp.ValueKind == JsonValueKind.True)
            {
                string? reason = doc.RootElement.TryGetProperty("refusalReason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                    ? reasonProp.GetString()
                    : null;
                return (true, reason ?? "draft refused");
            }
        }
        catch (JsonException)
        {
            // Unparseable draft payload -> treated as a normal (non-refusal) draft.
        }

        return (false, null);
    }

    private sealed record DegradedOutcome(
        bool Finished,
        WorkflowRun Run,
        AgentStep? DraftStep,
        List<AgentStep> Steps);

    private sealed class AgentStageFailureException : Exception
    {
        public AgentRole Role { get; }

        public string? Error { get; }

        public AgentStageFailureException(AgentRole role, string? error)
            : base($"Agent stage '{role}' failed: {error}")
        {
            Role = role;
            Error = error;
        }
    }

    private sealed class MaxIterationsExceededException : Exception
    {
        public MaxIterationsExceededException(int maxIterations)
            : base($"Workflow exceeded the maximum iteration cap of {maxIterations}.")
        {
        }
    }
}