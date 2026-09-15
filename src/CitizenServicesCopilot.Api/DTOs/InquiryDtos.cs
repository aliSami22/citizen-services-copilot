using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.ValueObjects;

namespace CitizenServicesCopilot.Api.DTOs;

public record SubmitInquiryRequest(string UserId, string Question);

public record InquiryDraftResponse(
    Guid Id,
    Guid InquiryId,
    string EligibilitySummary,
    string ProcedureSteps,
    string RequiredDocuments,
    string FeesAndTimeline,
    List<Citation> Citations,
    bool IsRefusal,
    string? RefusalReason,
    DateTime CreatedAtUtc
)
{
    public static InquiryDraftResponse FromEntity(InquiryDraft draft) =>
        new(
            draft.Id,
            draft.InquiryId,
            draft.EligibilitySummary,
            draft.ProcedureSteps,
            draft.RequiredDocuments,
            draft.FeesAndTimeline,
            draft.Citations,
            draft.IsRefusal,
            draft.RefusalReason,
            draft.CreatedAtUtc
        );
}

public record InquiryResponse(
    Guid Id,
    string UserId,
    string Question,
    string Status,
    string RoutedModel,
    decimal EstimatedCostUsd,
    decimal ActualCostUsd,
    DateTime CreatedAtUtc,
    InquiryDraftResponse? Draft
)
{
    public static InquiryResponse FromEntity(Inquiry inquiry) =>
        new(
            inquiry.Id,
            inquiry.UserId,
            inquiry.Question,
            inquiry.Status.ToString(),
            inquiry.RoutedModel,
            inquiry.EstimatedCostUsd,
            inquiry.ActualCostUsd,
            inquiry.CreatedAtUtc,
            inquiry.Draft != null ? InquiryDraftResponse.FromEntity(inquiry.Draft) : null
        );
}
