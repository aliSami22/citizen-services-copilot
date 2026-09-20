using System.Threading.Channels;
using CitizenServicesCopilot.Application.Common.Interfaces;
using CitizenServicesCopilot.Application.Common.Models;

namespace CitizenServicesCopilot.Api.Streaming;

/// <summary>
/// Channels workflow progress events from the orchestrator into an in-memory
/// <see cref="Channel{T}"/>. A request handler (e.g. the SSE stream endpoint)
/// owns the channel and drains it for the connected client.
/// </summary>
public sealed class ChannelWorkflowProgressSink : IWorkflowProgressSink
{
    private readonly Channel<WorkflowEvent> _channel;

    public ChannelWorkflowProgressSink(Channel<WorkflowEvent> channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public Task PublishAsync(WorkflowEvent evt, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(evt, ct).AsTask();

    /// <summary>
    /// Signals that no further events will be produced; read loops exit once
    /// the drained channel is empty.
    /// </summary>
    public void Complete() => _channel.Writer.TryComplete();
}