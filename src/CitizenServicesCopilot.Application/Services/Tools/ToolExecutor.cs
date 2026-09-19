using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// Validates tool arguments against the registered schema, then dispatches to
/// the matching <see cref="ITool"/>. Validation happens BEFORE execution so a
/// malformed call never reaches a tool (B2 requirement).
/// </summary>
public sealed class ToolExecutor : IToolExecutor
{
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly IToolSchemaValidator _validator;

    public ToolExecutor(IEnumerable<ITool> tools, IToolSchemaValidator validator)
    {
        _tools = (tools ?? throw new ArgumentNullException(nameof(tools)))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement args, CancellationToken ct = default)
    {
        _validator.Validate(toolName, args);

        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return ToolResult.Failed($"Tool '{toolName}' is not registered.");
        }

        return await tool.ExecuteAsync(args, ct);
    }
}