namespace CitizenServicesCopilot.Domain.ValueObjects;

public record Citation(
    string DocumentTitle,
    string Source,
    int PageNumber,
    string Section,
    string QuoteSnippet
);
