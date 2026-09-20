namespace CitizenServicesCopilot.Application.Common.Exceptions;

public sealed class ApprovalAlreadyDecidedException : Exception
{
    public Guid RunId { get; }

    public ApprovalAlreadyDecidedException(Guid runId, string decision)
        : base($"Run '{runId}' already has an approval decision: {decision}.")
    {
        RunId = runId;
    }
}