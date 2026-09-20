using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Application.Services.Tools;

public sealed class ToolSchemaValidator : IToolSchemaValidator
{
    private readonly IReadOnlyDictionary<string, ToolSchema> _schemas;

    public ToolSchemaValidator(IEnumerable<ToolSchema> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        _schemas = schemas.ToDictionary(s => s.ToolName, StringComparer.Ordinal);
    }

    public void Validate(string toolName, JsonElement args)
    {
        if (!_schemas.TryGetValue(toolName, out var schema))
        {
            throw new ToolSchemaValidationException(toolName, "no schema is registered for this tool.");
        }

        if (args.ValueKind != JsonValueKind.Object)
        {
            throw new ToolSchemaValidationException(toolName, "arguments must be a JSON object.");
        }

        foreach (var parameter in schema.Parameters)
        {
            var present = args.TryGetProperty(parameter.Name, out var value);

            if (!present)
            {
                if (parameter.IsRequired)
                {
                    throw new ToolSchemaValidationException(
                        toolName,
                        $"missing required field '{parameter.Name}'.");
                }

                continue;
            }

            if (value.ValueKind != parameter.Type)
            {
                throw new ToolSchemaValidationException(
                    toolName,
                    $"field '{parameter.Name}' must be of type {parameter.Type}, got {value.ValueKind}.");
            }
        }

        if (!schema.AllowAdditionalProperties)
        {
            foreach (var property in args.EnumerateObject())
            {
                if (!schema.Parameters.Any(p => p.Name == property.Name))
                {
                    throw new ToolSchemaValidationException(
                        toolName,
                        $"unexpected field '{property.Name}'.");
                }
            }
        }
    }
}