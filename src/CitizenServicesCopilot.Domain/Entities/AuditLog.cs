using CitizenServicesCopilot.Domain.Enums;

namespace CitizenServicesCopilot.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InquiryId { get; set; }
    public Inquiry? Inquiry { get; set; }

    public string OfficerId { get; set; } = string.Empty;
    public string OfficerName { get; set; } = string.Empty;
    public OfficerAction Action { get; set; }
    public string? Notes { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
