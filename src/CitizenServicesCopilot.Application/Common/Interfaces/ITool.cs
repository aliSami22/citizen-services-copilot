using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

public interface ITool
{
    string Name { get; }

    bool IsWrite { get; }

    Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct);
}