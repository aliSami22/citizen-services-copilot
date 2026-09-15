using CitizenServicesCopilot.Application.Agents;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Orchestration;
using CitizenServicesCopilot.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CitizenServicesCopilot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Cost Governor Service
        services.AddScoped<ICostGovernor, CostGovernorService>();

        // Specialized Agents
        services.AddScoped<EligibilityIdentifierAgent>();
        services.AddScoped<ProcedureResolverAgent>();
        services.AddScoped<ResponseDrafterAgent>();

        // Orchestrator & Human Review
        services.AddScoped<OrchestratorService>();
        services.AddScoped<HumanReviewService>();

        // Document Ingestion & Chunking (FR-1 baseline)
        services.AddTransient<Common.Interfaces.Ingestion.IDocumentExtractor, Services.Ingestion.PlainTextExtractor>();
        services.AddTransient<Common.Interfaces.Ingestion.IDocumentChunker, Services.Ingestion.WordOverlapChunker>();

        return services;
    }
}
