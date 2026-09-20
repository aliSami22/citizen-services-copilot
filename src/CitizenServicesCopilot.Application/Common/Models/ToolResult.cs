using System.Text.Json;

namespace CitizenServicesCopilot.Application.Common.Models;

public sealed record ToolResult(
    bool Success,
    string? Error,
    JsonElement Payload,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ToolResult Succeeded(
        JsonElement payload,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(true, null, payload, metadata ?? new Dictionary<string, string>());

    public static ToolResult Failed(string error, IReadOnlyDictionary<string, string>? metadata = null)
        => new(false, error, default, metadata ?? new Dictionary<string, string>());
}