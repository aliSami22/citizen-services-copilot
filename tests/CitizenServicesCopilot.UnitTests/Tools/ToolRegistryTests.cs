using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Tools;

namespace CitizenServicesCopilot.UnitTests.Tools;

public class ToolRegistryTests
{
    [Fact]
    public void Contains_ResolvesRegisteredTool()
    {
        var registry = new ToolRegistry(new[] { new StubTool("compute_fee") });

        Assert.True(registry.Contains("compute_fee"));
        Assert.False(registry.Contains("missing"));
        Assert.True(registry.TryGet("compute_fee", out var tool));
        Assert.Equal("compute_fee", tool.Name);
        Assert.Same(tool, registry.Get("compute_fee"));
        Assert.Null(registry.Get("missing"));
        Assert.Equal(new[] { "compute_fee" }, registry.ToolNames);
    }

    [Fact]
    public void DuplicateNames_Throw()
    {
        Assert.Throws<ArgumentException>(() => new ToolRegistry(new[]
        {
            new StubTool("dup"),
            new StubTool("dup")
        }));
    }

    private sealed class StubTool : ITool
    {
        public StubTool(string name) => Name = name;

        public string Name { get; }

        public bool IsWrite { get; } = false;

        public Task<ToolResult> ExecuteAsync(System.Text.Json.JsonElement args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Succeeded(System.Text.Json.JsonSerializer.SerializeToElement(new { ok = true })));
    }
}