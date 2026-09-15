using CitizenServicesCopilot.Domain.ValueObjects;

namespace CitizenServicesCopilot.Domain.Entities;

public class InquiryDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InquiryId { get; set; }
    public Inquiry? Inquiry { get; set; }

    public string EligibilitySummary { get; set; } = string.Empty;
    public string ProcedureSteps { get; set; } = string.Empty;
    public string RequiredDocuments { get; set; } = string.Empty;
    public string FeesAndTimeline { get; set; } = string.Empty;

    public List<Citation> Citations { get; set; } = new();

    public bool IsRefusal { get; set; }
    public string? RefusalReason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
