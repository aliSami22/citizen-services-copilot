using System.Text.Json;
using CitizenServicesCopilot.Application.Common;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// Performs a document-specific fee computation entirely in C#. Deterministic,
/// no LLM calls, no prompts.
/// </summary>
public sealed class ComputeFeeTool : ITool
{
    public string Name => ToolCatalog.ComputeFee;

    public bool IsWrite => false;

    public static ToolSchema Schema { get; } = new(
        ToolName: ToolCatalog.ComputeFee,
        Parameters: new[]
        {
            new ToolParameterSpec("serviceType", JsonValueKind.String, IsRequired: true),
            new ToolParameterSpec("parameters", JsonValueKind.Object, IsRequired: true)
        });

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("serviceType", out var serviceTypeProp) ||
            serviceTypeProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(serviceTypeProp.GetString()))
        {
            return Task.FromResult(ToolResult.Failed("missing or invalid required field 'serviceType'."));
        }

        var serviceType = serviceTypeProp.GetString()!.Trim().ToLowerInvariant();

        if (!args.TryGetProperty("parameters", out var parametersProp) || parametersProp.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult(ToolResult.Failed("field 'parameters' must be a JSON object."));
        }

        var (fee, error) = FeeEngine.Compute(serviceType, parametersProp);
        if (error is not null)
        {
            return Task.FromResult(ToolResult.Failed(error));
        }

        var payload = JsonSerializer.SerializeToElement(fee, JsonOptions.CamelCase);
        return Task.FromResult(ToolResult.Succeeded(payload));
    }
}