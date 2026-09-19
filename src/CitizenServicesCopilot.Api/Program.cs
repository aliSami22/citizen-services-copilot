using CitizenServicesCopilot.Api.DTOs;
using CitizenServicesCopilot.Application;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Orchestration;
using CitizenServicesCopilot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI reads the existing .NET 10 OpenAPI document (/openapi/v1.json).
    // Swashbuckle is used only for the UI, never as a second spec generator.
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/openapi/v1.json", "Citizen Services Copilot v1");
        c.RoutePrefix = "swagger";
    });
}

// In Development no HTTPS port is configured, so UseHttpsRedirection cannot resolve
// a redirect target and logs "Failed to determine the https port for redirect".
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapPost("/api/inquiries", async (
    SubmitInquiryRequest request,
    OrchestratorService orchestrator,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { message = "UserId and Question are required." });
    }

    try
    {
        var inquiry = await orchestrator.ProcessInquiryAsync(request.UserId, request.Question, ct);
        return Results.Ok(InquiryResponse.FromEntity(inquiry));
    }
    catch (BudgetExceededException ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: StatusCodes.Status402PaymentRequired,
            title: "Budget Exceeded");
    }
});

app.MapPost("/api/documents/text", async (
    IngestTextRequest request,
    CitizenServicesCopilot.Application.Common.Interfaces.Ingestion.IDocumentIngestionService ingestionService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) ||
        string.IsNullOrWhiteSpace(request.Source) ||
        string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest(new { message = "Title, Source, and Content are required fields." });
    }

    var command = new CitizenServicesCopilot.Application.Common.Models.IngestTextCommand(
        Title: request.Title.Trim(),
        Source: request.Source.Trim(),
        Version: string.IsNullOrWhiteSpace(request.Version) ? "1.0" : request.Version.Trim(),
        Category: string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim(),
        Content: request.Content.Trim()
    );

    var result = await ingestionService.IngestTextAsync(command, ct);

    var response = new DocumentIngestionResponse(
        DocumentId: result.DocumentId,
        Status: result.Status.ToString(),
        ChunkCount: result.ChunkCount,
        ContentHash: result.ContentHash,
        IsDuplicate: result.IsDuplicate,
        FailureReason: result.FailureReason
    );

    if (result.Status == CitizenServicesCopilot.Domain.Enums.IngestionStatus.Failed)
    {
        return Results.UnprocessableEntity(response);
    }

    return result.IsDuplicate
        ? Results.Ok(response)
        : Results.Created($"/api/documents/{result.DocumentId}", response);
})
.WithName("IngestTextDocument");

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

app.Run();

