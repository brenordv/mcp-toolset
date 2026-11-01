using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

/// <summary>
/// A hand-rolled <see cref="ILoggerFactory"/> that keeps the level and formatted message of every record its loggers
/// receive, so a test can assert on what a tool writes to the server log.
/// </summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    /// <summary>Every record's level and formatted message, in arrival order, across every category.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_entries);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => entries.Enqueue((logLevel, formatter(state, exception)));

        IDisposable ILogger.BeginScope<TState>(TState state) => null;
    }
}