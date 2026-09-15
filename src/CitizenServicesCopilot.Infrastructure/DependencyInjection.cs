using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Infrastructure.Llm;
using CitizenServicesCopilot.Infrastructure.Persistence;
using CitizenServicesCopilot.Infrastructure.Persistence.Repositories;
using CitizenServicesCopilot.Infrastructure.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // 1. PostgreSQL with pgvector DbContext
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
            });
        });

        // 2. Repositories
        services.AddScoped<IInquiryRepository, InquiryRepository>();
        services.AddScoped<IUserBudgetRepository, UserBudgetRepository>();

        // 3. Grounded Retriever
        services.AddScoped<IRetrievalService, GroundedRetriever>();

        // 4. Swappable LLM Provider Configuration
        var llmSection = configuration.GetSection(LlmOptions.SectionName);
        var llmOptions = llmSection.Get<LlmOptions>() ?? new LlmOptions();

        services.AddHttpClient();

        if (llmOptions.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<ILLMProvider>(sp =>
            {
                var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var logger = sp.GetRequiredService<ILogger<OpenAiLlmProvider>>();
                return new OpenAiLlmProvider(clientFactory.CreateClient(), llmOptions.OpenAI, logger);
            });
        }
        else
        {
            // Default to Ollama
            services.AddScoped<ILLMProvider>(sp =>
            {
                var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var logger = sp.GetRequiredService<ILogger<OllamaLlmProvider>>();
                return new OllamaLlmProvider(clientFactory.CreateClient(), llmOptions.Ollama, logger);
            });
        }

        return services;
    }
}
