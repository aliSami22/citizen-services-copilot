using CitizenServicesCopilot.Domain.Enums;

namespace CitizenServicesCopilot.Domain.Entities;

public class Inquiry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public InquiryStatus Status { get; set; } = InquiryStatus.Submitted;

    public string RoutedModel { get; set; } = string.Empty;
    public decimal EstimatedCostUsd { get; set; }
    public decimal ActualCostUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public InquiryDraft? Draft { get; set; }
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
