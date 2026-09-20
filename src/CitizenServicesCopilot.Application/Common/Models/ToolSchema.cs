using System.Text.Json;

namespace CitizenServicesCopilot.Application.Common.Models;

public sealed record ToolParameterSpec(
    string Name,
    JsonValueKind Type,
    bool IsRequired);

public sealed record ToolSchema(
    string ToolName,
    IReadOnlyList<ToolParameterSpec> Parameters,
    bool AllowAdditionalProperties = false);