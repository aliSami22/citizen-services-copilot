using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Enums;

namespace CitizenServicesCopilot.Application.Services;

public class HumanReviewService
{
    private readonly IInquiryRepository _inquiryRepository;

    public HumanReviewService(IInquiryRepository inquiryRepository)
    {
        _inquiryRepository = inquiryRepository;
    }

    public async Task<Inquiry> ApproveInquiryAsync(
        Guid inquiryId,
        string officerId,
        string officerName,
        string? notes = null,
        CancellationToken ct = default)
    {
        var inquiry = await _inquiryRepository.GetByIdAsync(inquiryId, ct)
            ?? throw new EntityNotFoundException(nameof(Inquiry), inquiryId);

        inquiry.Status = InquiryStatus.Approved;
        var audit = new AuditLog
        {
            InquiryId = inquiry.Id,
            OfficerId = officerId,
            OfficerName = officerName,
            Action = OfficerAction.Approved,
            Notes = notes,
            TimestampUtc = DateTime.UtcNow
        };

        inquiry.AuditLogs.Add(audit);
        await _inquiryRepository.UpdateAsync(inquiry, ct);
        return inquiry;
    }

    public async Task<Inquiry> RejectInquiryAsync(
        Guid inquiryId,
        string officerId,
        string officerName,
        string reason,
        CancellationToken ct = default)
    {
        var inquiry = await _inquiryRepository.GetByIdAsync(inquiryId, ct)
            ?? throw new EntityNotFoundException(nameof(Inquiry), inquiryId);

        inquiry.Status = InquiryStatus.Rejected;
        var audit = new AuditLog
        {
            InquiryId = inquiry.Id,
            OfficerId = officerId,
            OfficerName = officerName,
            Action = OfficerAction.Rejected,
            Notes = reason,
            TimestampUtc = DateTime.UtcNow
        };

        inquiry.AuditLogs.Add(audit);
        await _inquiryRepository.UpdateAsync(inquiry, ct);
        return inquiry;
    }
}
