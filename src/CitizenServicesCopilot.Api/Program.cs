using System.IdentityModel.Tokens.Jwt;
using System.Text;
using CitizenServicesCopilot.Api.DTOs;
using CitizenServicesCopilot.Api.Endpoints;
using CitizenServicesCopilot.Api.Middleware;
using CitizenServicesCopilot.Application;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Orchestration;
using CitizenServicesCopilot.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddWorkflowServices();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// JWT bearer authentication. The signing key must be 256 bits (32 bytes) or
// longer. In Development it comes from appsettings.Development.json / user
// secrets; production must override Jwt:Key via the JWT__KEY environment
// variable or a secret store. Never ship a real key in appsettings.json.
// Options are wired lazily (first request) so signer and validator always read
// the same resolved configuration and key rotation is a restart-free exercise.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var jwt = builder.Configuration.GetSection("Jwt");
    var key = jwt["Key"];
    if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
    {
        throw new InvalidOperationException(
            "Jwt:Key is missing or shorter than 32 bytes. Configure it via user secrets " +
            "(dotnet user-secrets set Jwt:Key \"...\"), appsettings, or the JWT__KEY environment variable.");
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwt["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
    };
});

builder.Services.AddAuthorization(options =>
{
    // Officer role gates the human-review and spend endpoints. Citizens are any
    // authenticated user (ownership is enforced per-endpoint).
    options.AddPolicy("Officer", policy =>
        policy.RequireAuthenticatedUser().RequireRole("Officer"));
});

var app = builder.Build();

// Propagate the correlation ID from the request header (or a fresh Guid) into
// the scoped ICorrelationContext used by the orchestrator and agents.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

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

app.MapWorkflowEndpoints();
app.MapAuthEndpoints(builder.Configuration);

app.Run();

