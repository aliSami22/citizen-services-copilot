using CitizenServicesCopilot.Application.Common.Interfaces;

namespace CitizenServicesCopilot.Application.Services.Tools;

/// <summary>
/// In-memory registry of the tools registered in the composition root.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly IReadOnlyCollection<string> _toolNames;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        var materialized = tools?.ToArray() ?? throw new ArgumentNullException(nameof(tools));
        _tools = materialized.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        _toolNames = materialized.Select(tool => tool.Name).ToArray();
    }

    public IReadOnlyCollection<string> ToolNames => _toolNames;

    public bool Contains(string toolName) => _tools.ContainsKey(toolName);

    public bool TryGet(string toolName, out ITool tool) => _tools.TryGetValue(toolName, out tool!);

    public ITool? Get(string toolName) => _tools.TryGetValue(toolName, out var tool) ? tool : null;
}