namespace CitizenServicesCopilot.Api.DTOs;

public record IngestTextRequest(
    string Title,
    string Source,
    string? Version,
    string? Category,
    string Content
);

public record DocumentIngestionResponse(
    Guid DocumentId,
    string Status,
    int ChunkCount,
    string ContentHash,
    bool IsDuplicate,
    string? FailureReason = null
);
