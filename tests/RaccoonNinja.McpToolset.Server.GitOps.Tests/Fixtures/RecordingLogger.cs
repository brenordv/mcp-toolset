using Microsoft.Extensions.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// A hand-rolled <see cref="ILogger"/> that keeps the level and formatted message of every record, and every scope opened
/// with a field dictionary, so a test can assert on what the server logs.
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly List<IReadOnlyDictionary<string, object>> _scopes = [];

    /// <summary>Every record's level and formatted message, in arrival order.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

    /// <summary>Every scope opened with a field dictionary, in arrival order.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object>> Scopes => [.. _scopes];

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        => _entries.Add((logLevel, formatter(state, exception)));

    IDisposable ILogger.BeginScope<TState>(TState state)
    {
        if (state is IReadOnlyDictionary<string, object> scope)
        {
            _scopes.Add(scope);
        }

        return null;
    }
}