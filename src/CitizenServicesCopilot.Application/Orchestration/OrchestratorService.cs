using CitizenServicesCopilot.Application.Agents;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Domain.Entities;
using CitizenServicesCopilot.Domain.Enums;

namespace CitizenServicesCopilot.Application.Orchestration;

public class OrchestratorService
{
    private readonly ICostGovernor _costGovernor;
    private readonly IRetrievalService _retrievalService;
    private readonly EligibilityIdentifierAgent _eligibilityAgent;
    private readonly ProcedureResolverAgent _procedureAgent;
    private readonly ResponseDrafterAgent _drafterAgent;
    private readonly IInquiryRepository _inquiryRepository;
    private readonly IUserBudgetRepository _budgetRepository;

    public OrchestratorService(
        ICostGovernor costGovernor,
        IRetrievalService retrievalService,
        EligibilityIdentifierAgent eligibilityAgent,
        ProcedureResolverAgent procedureAgent,
        ResponseDrafterAgent drafterAgent,
        IInquiryRepository inquiryRepository,
        IUserBudgetRepository budgetRepository)
    {
        _costGovernor = costGovernor;
        _retrievalService = retrievalService;
        _eligibilityAgent = eligibilityAgent;
        _procedureAgent = procedureAgent;
        _drafterAgent = drafterAgent;
        _inquiryRepository = inquiryRepository;
        _budgetRepository = budgetRepository;
    }

    public async Task<Inquiry> ProcessInquiryAsync(string userId, string question, CancellationToken ct = default)
    {
        // 1. Cost Governor: Classification, Routing, and Hard Cutoff Enforcement
        var costEstimate = await _costGovernor.EvaluateAndEnforceBudgetAsync(userId, question, ct);

        var inquiry = new Inquiry
        {
            UserId = userId,
            Question = question,
            RoutedModel = costEstimate.ModelName,
            EstimatedCostUsd = costEstimate.EstimatedCostUsd,
            Status = InquiryStatus.Submitted
        };

        // 2. Grounded Retrieval
        var relevantChunks = await _retrievalService.RetrieveRelevantChunksAsync(question, topK: 4, ct: ct);

        // 3. Corpus Guardrail: If no evidence is found, immediate refusal without wasting LLM calls
        if (relevantChunks.Count == 0)
        {
            inquiry.Status = InquiryStatus.Refused;
            inquiry.ActualCostUsd = 0.0m;
            inquiry.Draft = new InquiryDraft
            {
                InquiryId = inquiry.Id,
                IsRefusal = true,
                RefusalReason = ResponseDrafterAgent.StandardRefusalPhrase,
                EligibilitySummary = ResponseDrafterAgent.StandardRefusalPhrase,
                ProcedureSteps = ResponseDrafterAgent.StandardRefusalPhrase,
                RequiredDocuments = ResponseDrafterAgent.StandardRefusalPhrase,
                FeesAndTimeline = ResponseDrafterAgent.StandardRefusalPhrase
            };

            await _inquiryRepository.AddAsync(inquiry, ct);
            return inquiry;
        }

        // 4. Dispatch to Agent 1: Eligibility Identifier
        var (eligibilitySummary, eligTokens) = await _eligibilityAgent.IdentifyEligibilityAsync(
            question, relevantChunks, costEstimate.ModelName, ct);

        // 5. Dispatch to Agent 2: Procedure Resolver
        var (requiredDocs, steps, feesTimeline, procTokens) = await _procedureAgent.ResolveProcedureAsync(
            question, relevantChunks, costEstimate.ModelName, ct);

        // 6. Dispatch to Agent 3: Response Drafter
        var (draft, draftTokens) = await _drafterAgent.DraftResponseAsync(
            question, 
            eligibilitySummary, 
            $"{requiredDocs}\n{steps}\n{feesTimeline}", 
            relevantChunks, 
            costEstimate.ModelName, 
            ct);

        draft.InquiryId = inquiry.Id;
        inquiry.Draft = draft;
        inquiry.Status = draft.IsRefusal ? InquiryStatus.Refused : InquiryStatus.Drafted;

        // 7. Calculate Actual Token Cost & Deduct from User Budget
        int totalTokensUsed = eligTokens + procTokens + draftTokens;
        decimal actualCost = _costGovernor.CalculateActualCost(costEstimate.ModelTier, totalTokensUsed / 2, totalTokensUsed / 2);
        inquiry.ActualCostUsd = actualCost;

        var userBudget = await _budgetRepository.GetByUserIdAsync(userId, ct);
        if (userBudget != null)
        {
            userBudget.RecordUsage(totalTokensUsed, actualCost);
            await _budgetRepository.UpdateAsync(userBudget, ct);
        }

        // 8. Persist Inquiry
        await _inquiryRepository.AddAsync(inquiry, ct);
        return inquiry;
    }
}
