using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

/// <summary>
/// A hand-rolled <see cref="ILoggerFactory"/> that keeps every formatted message its loggers receive, so a
/// test can assert on what a tool writes to the server log.
/// </summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<string> _messages = new();

    /// <summary>Every formatted message logged through this factory, in arrival order.</summary>
    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_messages);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => messages.Enqueue(formatter(state, exception));

        IDisposable ILogger.BeginScope<TState>(TState state) => null;
    }
}