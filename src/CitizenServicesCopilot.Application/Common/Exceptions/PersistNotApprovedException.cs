namespace CitizenServicesCopilot.Application.Common.Exceptions;

/// <summary>
/// Thrown by the write-gated <c>persist_draft</c> tool when no Approved or
/// EditedAndApproved approval record exists for the run.
/// </summary>
public sealed class PersistNotApprovedException : Exception
{
    public Guid RunId { get; }

    public PersistNotApprovedException(Guid runId)
        : base($"Draft for run '{runId}' cannot be persisted until an approval record exists.")
    {
        RunId = runId;
    }
}