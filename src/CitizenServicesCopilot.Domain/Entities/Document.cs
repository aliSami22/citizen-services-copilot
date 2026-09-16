using CitizenServicesCopilot.Domain.Enums;

namespace CitizenServicesCopilot.Domain.Entities;

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Category { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public IngestionStatus Status { get; set; } = IngestionStatus.Processing;
    public string? FailureReason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}
