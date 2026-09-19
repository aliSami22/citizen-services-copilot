namespace CitizenServicesCopilot.Application.Common.Interfaces;

/// <summary>
/// Central lookup for registered tools. The orchestrator resolves write tools
/// by name and agents' <c>AllowedTools</c> are validated against it.
/// </summary>
public interface IToolRegistry
{
    IReadOnlyCollection<string> ToolNames { get; }

    bool Contains(string toolName);

    bool TryGet(string toolName, out ITool tool);

    ITool? Get(string toolName);
}