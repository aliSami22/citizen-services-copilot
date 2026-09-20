using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace CitizenServicesCopilot.Api.Tests;

/// <summary>
/// Test-only logger provider that records the CorrelationId found in each BeginScope
/// together with the messages logged inside that scope. Lets D4 assert that the
/// request correlation ID reaches the loggers' scopes end to end.
/// </summary>
public sealed class ScopeCapturingLoggerProvider : ILoggerProvider
{
    private sealed record ScopeRecord(Guid CorrelationId, string Message);

    private readonly ConcurrentBag<ScopeRecord> _records = new();

    public bool ContainsCorrelation(Guid correlationId)
        => _records.Any(r => r.CorrelationId == correlationId);

    public int RecordCount => _records.Count;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_records);

    public void Dispose() { }

    private sealed class CapturingLogger : ILogger
    {
        private static readonly AsyncLocal<Guid?> CurrentCorrelation = new();

        private readonly ConcurrentBag<ScopeRecord> _records;

        public CapturingLogger(ConcurrentBag<ScopeRecord> records) => _records = records;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var previous = CurrentCorrelation.Value;
            foreach (var kv in AsKeyValues(state))
            {
                if (kv.Key == "CorrelationId" && kv.Value is Guid guid)
                {
                    CurrentCorrelation.Value = guid;
                    if (previous != guid)
                    {
                        _records.Add(new ScopeRecord(guid, "(scope)"));
                    }
                }
            }

            return new ScopeDisposable(() => CurrentCorrelation.Value = previous);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Information || CurrentCorrelation.Value is not { } correlation)
            {
                return;
            }

            _records.Add(new ScopeRecord(correlation, formatter(state, exception)));
        }

        private static IEnumerable<KeyValuePair<string, object?>> AsKeyValues(object? state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                return pairs;
            }

            // Anonymous scope objects like BeginScope(new { CorrelationId }) are
            // passed through as plain objects; reflect over their properties.
            if (state is not null && state.GetType().GetProperty("CorrelationId") is { } prop)
            {
                return new[] { new KeyValuePair<string, object?>("CorrelationId", prop.GetValue(state)) };
            }

            return Array.Empty<KeyValuePair<string, object?>>();
        }

        private sealed class ScopeDisposable : IDisposable
        {
            private readonly Action _restore;

            public ScopeDisposable(Action restore) => _restore = restore;

            public void Dispose() => _restore();
        }
    }
}