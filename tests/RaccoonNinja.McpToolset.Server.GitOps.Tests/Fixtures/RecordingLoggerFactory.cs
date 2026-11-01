using Microsoft.Extensions.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// A hand-rolled <see cref="ILoggerFactory"/> whose loggers are one <see cref="RecordingLogger"/>, so a test can assert on
/// what a tool call logs.
/// </summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly RecordingLogger _logger = new();

    /// <summary>Every record's level and formatted message, in arrival order, across every category.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _logger.Entries;

    public ILogger CreateLogger(string categoryName) => _logger;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}