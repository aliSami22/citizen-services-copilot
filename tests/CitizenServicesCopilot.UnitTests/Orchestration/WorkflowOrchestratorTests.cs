using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Orchestration;
using CitizenServicesCopilot.Application.Services.Tools;
using CitizenServicesCopilot.Domain.Agents;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Citation = CitizenServicesCopilot.Application.Common.Models.Citation;

namespace CitizenServicesCopilot.UnitTests.Orchestration;

public class WorkflowOrchestratorTests
{
    private const string ValidDraftJson =
        "{\"isRefusal\":false,\"refusalReason\":null,\"eligibilitySummary\":\"resident of the emirate\"," +
        "\"requiredDocuments\":\"passport\",\"procedureSteps\":\"1. apply\\n2. wait\",\"feesAndTimeline\":\"100 USD\"," +
        "\"citations\":[],\"tokensUsed\":10}";

    [Fact]
    public async Task HappyPath_RunsAllStagesInOrder_AndPersistsAfterApproval()
    {
        var steps = new InMemorySteps();
        var approvals = new InMemoryApprovals(new ApprovalRecord(
            Guid.NewGuid(), ApprovalDecision.Approved, DateTimeOffset.UtcNow, "approver", null, null));
        var agents = new IAgent[] { HappyEligibility(), HappyProcedure(), HappyDrafter() };
        var retrieval = new StubRetrieval(_ => SuccessRetrieval());
        var runs = new InMemoryRuns();
        var toolExecutor = new StubToolExecutor(succeed: true);
        var orchestrator = BuildOrchestrator(agents, retrieval, runs, steps, approvals, toolExecutor);

        var run = await orchestrator.RunAsync("user-1", "How do I get a passport?", "test-model");

        Assert.Equal(RunStatus.Approved, run.Status);
        Assert.Equal(1, retrieval.CallCount);
        Assert.Equal(1, toolExecutor.CallCount);

        Assert.Collection(steps.All,
            s => Assert.Equal(AgentRole.EligibilityIdentifier, s.Role),
            s => Assert.Equal(AgentRole.ProcedureResolver, s.Role),
            s => Assert.Equal(AgentRole.ResponseDrafter, s.Role),
            s =>
            {
                Assert.Equal(AgentRole.Persist, s.Role);
                Assert.Equal(AgentStepStatus.Succeeded, s.Status);
                Assert.Contains("persisted", s.OutputSummary);
            });
        Assert.DoesNotContain(steps.All, s => s.Status == AgentStepStatus.Degraded);
    }

    [Fact]
    public async Task MaxIterationsBreaker_TripsAndFailsRun()
    {
        var steps = new InMemorySteps();
        var agents = new IAgent[] { FailingEligibility(), HappyProcedure(), HappyDrafter() };
        var retrieval = new StubRetrieval(_ => SuccessRetrieval());
        var runs = new InMemoryRuns();
        var options = new OrchestratorOptions
        {
            MaxIterations = 2,
            RetryBaseDelayMs = 1,
            EnableGracefulDegradation = false
        };
        var orchestrator = BuildOrchestrator(agents, retrieval, runs, steps, new InMemoryApprovals((ApprovalRecord?)null), options: options);

        var run = await orchestrator.RunAsync("user-1", "Q", "test-model");

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(2, steps.All.Count);
        Assert.All(steps.All, s => Assert.Equal(AgentStepStatus.Failed, s.Status));
    }

    [Fact]
    public async Task StepTimeout_RetriesOnceThenFails()
    {
        var steps = new InMemorySteps();
        var agents = new IAgent[] { HangingEligibility(), HappyProcedure(), HappyDrafter() };
        var retrieval = new StubRetrieval(_ => SuccessRetrieval());
        var runs = new InMemoryRuns();
        var options = new OrchestratorOptions
        {
            StepTimeout = TimeSpan.FromMilliseconds(50),
            RetryAttempts = 1,
            RetryBaseDelayMs = 1,
            EnableGracefulDegradation = false
        };
        var orchestrator = BuildOrchestrator(agents, retrieval, runs, steps, new InMemoryApprovals((ApprovalRecord?)null), options: options);

        var run = await orchestrator.RunAsync("user-1", "Q", "test-model");

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("timed out", steps.All[0].ErrorMessage);
        Assert.All(steps.All, s => Assert.Equal(AgentStepStatus.Failed, s.Status));
    }

    [Fact]
    public async Task AgentChainFailure_DegradesWithRetrievalAndDraftAndDegradedStep()
    {
        var steps = new InMemorySteps();
        var approvals = new InMemoryApprovals(new ApprovalRecord(
            Guid.NewGuid(), ApprovalDecision.Approved, DateTimeOffset.UtcNow, "approver", null, null));
        var eligibility = FailingEligibility();
        var procedure = HappyProcedure();
        var drafter = HappyDrafter();
        var agents = new IAgent[] { eligibility, procedure, drafter };
        var retrieval = new StubRetrieval(_ => SuccessRetrieval());
        var runs = new InMemoryRuns();
        var orchestrator = BuildOrchestrator(agents, retrieval, runs, steps, approvals);

        var run = await orchestrator.RunAsync("user-1", "Q", "test-model");

        Assert.Equal(RunStatus.Approved, run.Status);
        Assert.Equal(2, retrieval.CallCount);
        Assert.Equal(3, eligibility.CallCount);
        Assert.Equal(0, procedure.CallCount);
        Assert.Equal(1, drafter.CallCount);

        var degraded = Assert.Single(steps.All, s => s.Status == AgentStepStatus.Degraded);
        Assert.Equal(AgentRole.EligibilityIdentifier, degraded.Role);
        Assert.Equal("graceful degradation to plain RAG after agent chain failure", degraded.OutputSummary);
    }

    [Fact]
    public async Task Degradation_WhenSecondRetrievalRefuses_FailsRun()
    {
        var steps = new InMemorySteps();
        var agents = new IAgent[] { FailingEligibility(), HappyProcedure(), HappyDrafter() };
        var retrieval = new StubRetrieval(call => call == 1 ? SuccessRetrieval() : RefusalRetrieval());
        var runs = new InMemoryRuns();
        var orchestrator = BuildOrchestrator(agents, retrieval, runs, steps, new InMemoryApprovals((ApprovalRecord?)null));

        var run = await orchestrator.RunAsync("user-1", "Q", "test-model");

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Single(steps.All, s => s.Status == AgentStepStatus.Degraded);
        Assert.DoesNotContain(steps.All, s => s.Role == AgentRole.ResponseDrafter);
    }

    [Fact]
    public async Task Cancellation_DuringStageStopsFurtherExecution()
    {
        var steps = new InMemorySteps();
        var approvals = new InMemoryApprovals((ApprovalRecord?)null);
        var procedureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var procedure = new StubAgent
        {
            Role = AgentRole.ProcedureResolver,
            Handler = async (_, ct) =>
            {
                procedureStarted.TrySetResult();
                await Task.Delay(10_000, ct);
                return SucceededStep(AgentRole.ProcedureResolver, "docs");
            }
        };
        var eligibility = HappyEligibility();
        var agents = new IAgent[] { eligibility, procedure, HappyDrafter() };
        var retrieval = new StubRetrieval(_ => SuccessRetrieval());
        var runs = new InMemoryRuns();
        var orchestrator = BuildOrchestrator(agents, retrieval, runs, steps, approvals);

        using var cts = new CancellationTokenSource();
        var runTask = orchestrator.RunAsync("user-1", "Q", "test-model", cts.Token);

        await procedureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

        var finalRun = runs.Last!;
        Assert.Equal(RunStatus.Cancelled, finalRun.Status);
        Assert.Single(steps.All);
        Assert.Equal(AgentRole.EligibilityIdentifier, steps.All[0].Role);
    }

    [Fact]
    public async Task RejectedApproval_TerminatesRunAndSkipsPersist()
    {
        var steps = new InMemorySteps();
        var approvals = new InMemoryApprovals(new ApprovalRecord(
            Guid.NewGuid(), ApprovalDecision.Rejected, DateTimeOffset.UtcNow, "approver", "denied", null));
        var agents = new IAgent[] { HappyEligibility(), HappyProcedure(), HappyDrafter() };
        var retrieval = new StubRetrieval(_ => SuccessRetrieval());
        var runs = new InMemoryRuns();
        var toolExecutor = new StubToolExecutor(succeed: true);
        var orchestrator = BuildOrchestrator(agents, retrieval, runs, steps, approvals, toolExecutor);

        var run = await orchestrator.RunAsync("user-1", "Q", "test-model");

        Assert.Equal(RunStatus.Rejected, run.Status);
        Assert.Equal(0, toolExecutor.CallCount);
    }

    [Fact]
    public async Task ApprovalTimeout_ExceedingMaxWait_FailsRunWithTimeoutReason()
    {
        var steps = new InMemorySteps();
        var agents = new IAgent[] { HappyEligibility(), HappyProcedure(), HappyDrafter() };
        var retrieval = new StubRetrieval(_ => SuccessRetrieval());
        var runs = new InMemoryRuns();
        var options = new OrchestratorOptions
        {
            ApprovalPollingInterval = TimeSpan.FromMilliseconds(20),
            ApprovalWaitTimeout = TimeSpan.FromMilliseconds(100)
        };
        var approvals = new InMemoryApprovals(_ => null);
        var toolExecutor = new StubToolExecutor(succeed: true);
        var orchestrator = BuildOrchestrator(
            agents, retrieval, runs, steps, approvals, toolExecutor, options: options);

        var run = await orchestrator.RunAsync("user-1", "Q", "test-model");

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("approval timeout", run.ErrorMessage);
        Assert.Equal(0, toolExecutor.CallCount);
    }

    [Fact]
    public async Task ApprovalArrivesMidWait_ResolvesAndPersistsEditedContent()
    {
        var steps = new InMemorySteps();
        var agents = new IAgent[] { HappyEligibility(), HappyProcedure(), HappyDrafter() };
        var retrieval = new StubRetrieval(_ => SuccessRetrieval());
        var runs = new InMemoryRuns();
        const string editedDraftJs = "{\"isRefusal\":false,\"refusalReason\":null,\"eligibilitySummary\":\"edited\"," +
                                     "\"requiredDocuments\":\"passport\",\"procedureSteps\":\"1. apply\"," +
                                     "\"feesAndTimeline\":\"edited fee note\",\"citations\":[],\"tokensUsed\":10}";
        var record = new ApprovalRecord(
            Guid.NewGuid(), ApprovalDecision.EditedAndApproved, DateTimeOffset.UtcNow, "approver", "see edits", editedDraftJs);
        var approvals = new InMemoryApprovals(call => call == 1 ? null : record);
        var options = new OrchestratorOptions
        {
            ApprovalPollingInterval = TimeSpan.FromMilliseconds(20)
        };
        var toolExecutor = new StubToolExecutor(succeed: true);
        var orchestrator = BuildOrchestrator(
            agents, retrieval, runs, steps, approvals, toolExecutor, options: options);

        var run = await orchestrator.RunAsync("user-1", "Q", "test-model");

        Assert.Equal(RunStatus.Approved, run.Status);
        Assert.Equal(editedDraftJs, toolExecutor.LastDraftJson);
        Assert.True(approvals.GetCalls > 1);
    }

    [Fact]
    public void Construction_AgentDeclaresUnregisteredTool_Throws()
    {
        var agent = new StubAgent
        {
            Role = AgentRole.ProcedureResolver,
            AllowedTools = new HashSet<string> { "search_corpus" },
            Handler = (_, _) => Completed(SucceededStep(AgentRole.ProcedureResolver, "docs"))
        };
        var registry = new ToolRegistry(new ITool[] { new StubPersistWriteTool() });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildOrchestrator(
                new IAgent[] { agent },
                new StubRetrieval(_ => SuccessRetrieval()),
                new InMemoryRuns(),
                new InMemorySteps(),
                new InMemoryApprovals((ApprovalRecord?)null),
                toolRegistry: registry));

        Assert.Contains("search_corpus", ex.Message);
    }

    private static WorkflowOrchestrator BuildOrchestrator(
        IReadOnlyList<IAgent> agents,
        IRetrievalService retrieval,
        InMemoryRuns runs,
        InMemorySteps steps,
        InMemoryApprovals approvals,
        IToolExecutor? toolExecutor = null,
        IToolRegistry? toolRegistry = null,
        OrchestratorOptions? options = null)
        => new(
            agents,
            retrieval,
            runs,
            steps,
            approvals,
            toolExecutor ?? new StubToolExecutor(succeed: true),
            toolRegistry ?? new ToolRegistry(new ITool[] { new StubPersistWriteTool() }),
            new AlwaysOkPreFlight(),
            options ?? new OrchestratorOptions(),
            NullLogger<WorkflowOrchestrator>.Instance);

    private static AgentStep SucceededStep(AgentRole role, string output)
        => new(role, AgentStepStatus.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 5, output, null);

    private static AgentStep FailedStep(AgentRole role, string error)
        => new(role, AgentStepStatus.Failed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, error);

    private static Task<AgentStep> Completed(AgentStep step) => Task.FromResult(step);

    private static Task<AgentStep> NeverCompleting()
        => new TaskCompletionSource<AgentStep>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    private static StubAgent HappyEligibility()
        => new()
        {
            Role = AgentRole.EligibilityIdentifier,
            Handler = (_, _) => Completed(SucceededStep(AgentRole.EligibilityIdentifier, "resident of the emirate"))
        };

    private static StubAgent FailingEligibility()
        => new()
        {
            Role = AgentRole.EligibilityIdentifier,
            Handler = (_, _) => Completed(FailedStep(AgentRole.EligibilityIdentifier, "LLM unavailable"))
        };

    private static StubAgent HangingEligibility()
        => new()
        {
            Role = AgentRole.EligibilityIdentifier,
            Handler = (_, _) => NeverCompleting()
        };

    private static StubAgent HappyProcedure()
        => new()
        {
            Role = AgentRole.ProcedureResolver,
            Handler = (_, _) => Completed(SucceededStep(AgentRole.ProcedureResolver, "docs: passport\nsteps: apply\nfees: 100"))
        };

    private static StubAgent HappyDrafter()
        => new()
        {
            Role = AgentRole.ResponseDrafter,
            Handler = (_, _) => Completed(SucceededStep(AgentRole.ResponseDrafter, ValidDraftJson))
        };

    private static RetrievalResult SuccessRetrieval()
    {
        var chunks = new[]
        {
            new ScoredChunk(
                new DocumentChunk { Content = "passport application requires residency", PageNumber = 1, Section = "S1" },
                DenseScore: 0.6, KeywordScore: 0.2, CombinedScore: 0.5)
        };
        return RetrievalResult.Success(chunks, Array.Empty<Citation>(), 0.5);
    }

    private static RetrievalResult RefusalRetrieval()
        => RetrievalResult.Refuse();

    private sealed class StubAgent : IAgent
    {
        public required AgentRole Role { get; init; }

        public IReadOnlySet<string> AllowedTools { get; init; } = new HashSet<string>();

        public required Func<AgentInput, CancellationToken, Task<AgentStep>> Handler { get; init; }

        public int CallCount { get; private set; }

        public Task<AgentStep> ExecuteAsync(AgentInput input, CancellationToken ct)
        {
            CallCount++;
            return Handler(input, ct);
        }
    }

    private sealed class StubRetrieval : IRetrievalService
    {
        private readonly Func<int, RetrievalResult> _factory;

        public int CallCount { get; private set; }

        public StubRetrieval(Func<int, RetrievalResult> factory) => _factory = factory;

        public Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_factory(CallCount));
        }
    }

    private sealed class StubToolExecutor : IToolExecutor
    {
        private readonly bool _succeed;

        public int CallCount { get; private set; }

        public string? LastDraftJson { get; private set; }

        public StubToolExecutor(bool succeed) => _succeed = succeed;

        public Task<ToolResult> ExecuteAsync(string toolName, JsonElement args, CancellationToken ct = default)
        {
            CallCount++;
            if (args.TryGetProperty("draftJson", out var draftJson) && draftJson.ValueKind == JsonValueKind.String)
            {
                LastDraftJson = draftJson.GetString();
            }

            return Task.FromResult(_succeed
                ? ToolResult.Succeeded(JsonSerializer.SerializeToElement(new { persisted = true }))
                : ToolResult.Failed("persist tool boom"));
        }
    }

    private sealed class StubPersistWriteTool : ITool
    {
        public string Name => WorkflowOrchestrator.PersistDraftToolName;

        public bool IsWrite => true;

        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Succeeded(JsonSerializer.SerializeToElement(new { persisted = true })));
    }

    private sealed class AlwaysOkPreFlight : IBudgetPreFlightCheck
    {
        public Task ThrowIfExceededAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryRuns : IWorkflowRunRepository
    {
        private readonly Dictionary<Guid, WorkflowRun> _store = new();

        public WorkflowRun? Last => _store.Values.LastOrDefault();

        public Task<WorkflowRun?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(runId, out var run) ? run : null);

        public Task AddAsync(WorkflowRun run, CancellationToken ct = default)
        {
            _store[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(WorkflowRun run, CancellationToken ct = default)
        {
            _store[run.Id] = run;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySteps : IAgentStepRepository
    {
        public List<AgentStep> All { get; } = new();

        public Task AddAsync(AgentStep step, CancellationToken ct = default)
        {
            All.Add(step);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentStep>> GetForRunAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<AgentStep>)All);
    }

    private sealed class InMemoryApprovals : IApprovalService
    {
        private readonly Func<int, ApprovalRecord?> _script;

        public int GetCalls { get; private set; }

        public InMemoryApprovals(ApprovalRecord? record)
            : this(_ => record)
        {
        }

        public InMemoryApprovals(Func<int, ApprovalRecord?> script) => _script = script;

        public Task<ApprovalRecord> ApproveAsync(string runId, string approverId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ApprovalRecord> RejectAsync(string runId, string approverId, string reason, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ApprovalRecord> EditAndApproveAsync(
            string runId, string approverId, string editedDraftJson, string? reason, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ApprovalRecord?> GetForRunAsync(string runId, CancellationToken ct = default)
        {
            GetCalls++;
            return Task.FromResult(_script(GetCalls));
        }
    }
}