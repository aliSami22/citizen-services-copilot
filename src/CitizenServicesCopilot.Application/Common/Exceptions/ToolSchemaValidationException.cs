namespace CitizenServicesCopilot.Application.Common.Exceptions;

public sealed class ToolSchemaValidationException(string toolName, string message)
    : Exception($"Tool '{toolName}' received invalid arguments: {message}")
{
    public string ToolName { get; } = toolName;
}