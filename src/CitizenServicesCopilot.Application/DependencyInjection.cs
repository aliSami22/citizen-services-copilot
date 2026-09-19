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

        // Prompt provider (embedded resources) and specialized agents
        services.AddSingleton<Common.Interfaces.IPromptProvider, Services.Prompts.EmbeddedResourcePromptProvider>();
        services.AddScoped<EligibilityIdentifierAgent>();
        services.AddScoped<ProcedureResolverAgent>();
        services.AddScoped<ResponseDrafterAgent>();
        services.AddScoped<IAgent>(sp => sp.GetRequiredService<EligibilityIdentifierAgent>());
        services.AddScoped<IAgent>(sp => sp.GetRequiredService<ProcedureResolverAgent>());
        services.AddScoped<IAgent>(sp => sp.GetRequiredService<ResponseDrafterAgent>());

        // Orchestrator & Human Review
        services.AddScoped<OrchestratorService>();
        services.AddScoped<HumanReviewService>();

        // Document Ingestion & Chunking (FR-1 baseline)
        services.AddTransient<Common.Interfaces.Ingestion.IDocumentExtractor, Services.Ingestion.PlainTextExtractor>();
        services.AddTransient<Common.Interfaces.Ingestion.IDocumentChunker, Services.Ingestion.WordOverlapChunker>();
        services.AddScoped<Common.Interfaces.Ingestion.IDocumentIngestionService, Services.Ingestion.DocumentIngestionService>();

        // Retrieval Enhancement (FR-2)
        services.AddSingleton<Common.Interfaces.Retrieval.IQueryEnhancer, Services.Retrieval.QueryEnhancer>();
        services.AddSingleton<Services.Retrieval.HybridFusionEngine>();

        // Tools (B4): read tools + the write-gated persist tool
        services.AddScoped<ITool, Services.Tools.SearchCorpusTool>();
        services.AddScoped<ITool, Services.Tools.GetRegulationVersionTool>();
        services.AddScoped<ITool, Services.Tools.ComputeFeeTool>();
        services.AddScoped<ITool, Services.Tools.PersistDraftTool>();
        services.AddScoped<IToolRegistry, Services.Tools.ToolRegistry>();
        services.AddSingleton<IToolSchemaValidator>(_ => new Services.Tools.ToolSchemaValidator(Services.Tools.ToolCatalog.Schemas));
        services.AddScoped<IToolExecutor, Services.Tools.ToolExecutor>();

        return services;
    }
}
