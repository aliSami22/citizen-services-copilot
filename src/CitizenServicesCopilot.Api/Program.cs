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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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
})
.WithName("SubmitInquiry");

app.Run();

