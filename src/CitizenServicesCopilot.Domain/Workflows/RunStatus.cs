namespace CitizenServicesCopilot.Domain.Workflows;

public enum RunStatus
{
    NotStarted = 0,
    Running,
    WaitingApproval,
    Approved,
    Rejected,
    Completed,
    Failed,
    Cancelled
}