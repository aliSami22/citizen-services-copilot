using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// Validates tool arguments against the registered schema, then dispatches to
/// the matching tool through the <see cref="IToolRegistry"/>. Validation
/// happens BEFORE execution so a malformed call never reaches a tool (B2).
/// </summary>
public sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry _registry;
    private readonly IToolSchemaValidator _validator;

    public ToolExecutor(IToolRegistry registry, IToolSchemaValidator validator)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement args, CancellationToken ct = default)
    {
        _validator.Validate(toolName, args);

        if (!_registry.TryGet(toolName, out var tool))
        {
            return ToolResult.Failed($"Tool '{toolName}' is not registered.");
        }

        return await tool.ExecuteAsync(args, ct);
    }
}