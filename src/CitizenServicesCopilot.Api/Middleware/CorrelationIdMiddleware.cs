using CitizenServicesCopilot.Application.Common.Interfaces;

namespace CitizenServicesCopilot.Api.Middleware;

/// <summary>
/// Sets the scoped <see cref="ICorrelationContext"/> from the X-Correlation-Id
/// request header (when parseable) or a freshly generated Guid, and echoes it
/// back on the response so callers can correlate logs end to end.
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        var header = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        correlation.CorrelationId = Guid.TryParse(header, out var parsed) ? parsed : Guid.NewGuid();

        context.Response.Headers["X-Correlation-Id"] = correlation.CorrelationId.ToString();

        await _next(context);
    }
}