using CitizenServicesCopilot.Domain.Workflows;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Persistence ports for the multi-agent workflow. EF Core implementations
/// land in Checkpoint B6; until then the orchestrator is exercised against
/// in-memory fakes.
/// </summary>
public interface IWorkflowRunRepository
{
    Task<WorkflowRun?> GetByIdAsync(Guid runId, CancellationToken ct = default);

    Task AddAsync(WorkflowRun run, CancellationToken ct = default);

    Task UpdateAsync(WorkflowRun run, CancellationToken ct = default);
}

public interface IAgentStepRepository
{
    Task AddAsync(AgentStep step, CancellationToken ct = default);

    Task<IReadOnlyList<AgentStep>> GetForRunAsync(Guid runId, CancellationToken ct = default);
}

public interface IApprovalRecordRepository
{
    Task<ApprovalAudit?> GetForRunAsync(Guid runId, CancellationToken ct = default);

    Task AddAsync(ApprovalAudit record, CancellationToken ct = default);
}