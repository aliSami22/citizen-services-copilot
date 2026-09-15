namespace CitizenServicesCopilot.Domain.Entities;

public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    public string Content { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string Section { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }

    public float[]? Embedding { get; set; }
}
