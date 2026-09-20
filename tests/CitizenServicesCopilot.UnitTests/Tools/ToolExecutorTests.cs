using System.Text.Json;
using CitizenServicesCopilot.Application.Common.Exceptions;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;
using CitizenServicesCopilot.Application.Services.Tools;

namespace CitizenServicesCopilot.UnitTests.Tools;

public class ToolExecutorTests
{
    [Fact]
    public async Task ValidArgs_ExecutesRegisteredTool()
    {
        var tool = new RecordingTool("echo", isWrite: false);
        var executor = new ToolExecutor(
            new ToolRegistry(new ITool[] { tool }),
            new ToolSchemaValidator(new[] { tool.Schema }));

        var result = await executor.ExecuteAsync("echo", JsonSerializer.SerializeToElement(new { text = "hi" }));

        Assert.True(result.Success);
        Assert.Equal(1, tool.CallCount);
    }

    [Fact]
    public async Task InvalidArgs_ThrowsSchemaException_AndToolIsNotCalled()
    {
        var tool = new RecordingTool("echo", isWrite: false);
        var executor = new ToolExecutor(
            new ToolRegistry(new ITool[] { tool }),
            new ToolSchemaValidator(new[] { tool.Schema }));

        var ex = await Assert.ThrowsAsync<ToolSchemaValidationException>(
            () => executor.ExecuteAsync("echo", JsonSerializer.SerializeToElement(new { })));

        Assert.Contains("missing required field 'text'", ex.Message);
        Assert.Equal(0, tool.CallCount);
    }

    [Fact]
    public async Task UnknownToolName_ThrowsSchemaException()
    {
        var tool = new RecordingTool("echo", isWrite: false);
        var executor = new ToolExecutor(
            new ToolRegistry(new ITool[] { tool }),
            new ToolSchemaValidator(new[] { tool.Schema }));

        var ex = await Assert.ThrowsAsync<ToolSchemaValidationException>(
            () => executor.ExecuteAsync("no_such_tool", JsonSerializer.SerializeToElement(new { })));

        Assert.Contains("no schema is registered", ex.Message);
        Assert.Equal(0, tool.CallCount);
    }

    private sealed class RecordingTool : ITool
    {
        public RecordingTool(string name, bool isWrite)
        {
            Name = name;
            IsWrite = isWrite;
        }

        public string Name { get; }

        public bool IsWrite { get; }

        public ToolSchema Schema { get; } = new(
            ToolName: "echo",
            Parameters: new[] { new ToolParameterSpec("text", JsonValueKind.String, IsRequired: true) });

        public int CallCount { get; private set; }

        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(ToolResult.Succeeded(JsonSerializer.SerializeToElement(new { echoed = true })));
        }
    }
}