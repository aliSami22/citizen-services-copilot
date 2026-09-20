using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Validates tool arguments against the registered schema and then dispatches to
/// the matching tool. Agents and the orchestrator never call <see cref="ITool"/>
/// directly.
/// </summary>
public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(string toolName, JsonElement args, CancellationToken ct = default);
}