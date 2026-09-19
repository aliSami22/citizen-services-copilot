using System.Text.Json;

namespace CitizenServicesCopilot.Application.Common;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}