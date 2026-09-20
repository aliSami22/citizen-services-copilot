namespace CitizenServicesCopilot.Domain.Workflows;

public enum AgentStepStatus
{
    Queued = 0,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Degraded
}