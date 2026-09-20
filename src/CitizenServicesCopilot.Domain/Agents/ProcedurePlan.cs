namespace CitizenServicesCopilot.Domain.Agents;

public sealed record ProcedurePlan(
    string RequiredDocuments,
    string Steps,
    string FeesAndTimeline,
    int TokensUsed);