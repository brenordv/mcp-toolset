using System.Text;
using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Runner;

public sealed class FilterTripwireTests
{
    private const string StatusSubcommand = "status";
    private const string SuspiciousFilterEvent = "suspicious_filter_invocation";
    private const string DriverNameField = "driver_name";
    private const string LeakProbeSegment = "leak-probe";
    private const int ProbeCallId = 7;

    private static readonly string[] StatusArgv = ["git", "-c", "core.fsmonitor=false", StatusSubcommand];

    [Theory]
    [InlineData("error: external filter 'cat' failed 1\n")]
    [InlineData("error: external filter ''/opt/filters/clean me.sh' %f' failed 1\n")]
    [InlineData("error: external filter 'printf x\nexit 1' failed 1\n")]
    public void Inspect_FailedFilterInStderr_LogsOneWarning(string stderr)
    {
        // Arrange
        var logger = new RecordingLogger();

        // Act
        FilterTripwire.Inspect(Encoding.UTF8.GetBytes(stderr), StatusArgv, ProbeCallId, logger);

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
        var scope = Assert.Single(logger.Scopes);
        Assert.Equal(SuspiciousFilterEvent, (string)scope[LogFields.Event]);
        Assert.Equal(StatusSubcommand, (string)scope[LogFields.Tool]);
        Assert.Equal(ProbeCallId, (int)scope[LogFields.CallId]);
    }

    [Fact]
    public void Inspect_FailedFilterWithPathInCommand_LogsNoCommandText()
    {
        // Arrange
        var logger = new RecordingLogger();
        var command = Path.Combine(Path.GetTempPath(), LeakProbeSegment, "clean.sh") + " %f";
        var stderr = Encoding.UTF8.GetBytes($"error: external filter '{command}' failed 1\n");

        // Act
        FilterTripwire.Inspect(stderr, StatusArgv, ProbeCallId, logger);

        // Assert
        var scope = Assert.Single(logger.Scopes);
        Assert.False(scope.ContainsKey(DriverNameField));
        Assert.DoesNotContain(scope.Values, value => value.ToString().Contains(LeakProbeSegment, StringComparison.Ordinal));
        Assert.DoesNotContain(LeakProbeSegment, Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git\n")]
    [InlineData("error: cannot fork to run external filter 'cat'\n")]
    public void Inspect_NoFailedFilterInStderr_LogsNothing(string stderr)
    {
        // Arrange
        var logger = new RecordingLogger();

        // Act
        FilterTripwire.Inspect(Encoding.UTF8.GetBytes(stderr), StatusArgv, ProbeCallId, logger);

        // Assert
        Assert.Empty(logger.Levels);
        Assert.Empty(logger.Scopes);
    }

    #region Test Helpers

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<LogLevel> _levels = [];
        private readonly List<string> _messages = [];
        private readonly List<IReadOnlyDictionary<string, object>> _scopes = [];

        public IReadOnlyList<LogLevel> Levels => _levels;

        public IReadOnlyList<string> Messages => _messages;

        public IReadOnlyList<IReadOnlyDictionary<string, object>> Scopes => _scopes;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _levels.Add(logLevel);
            _messages.Add(formatter(state, exception));
        }

        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            if (state is IReadOnlyDictionary<string, object> scope)
            {
                _scopes.Add(scope);
            }

            return null;
        }
    }

    #endregion
}