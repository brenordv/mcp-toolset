using RaccoonNinja.McpToolset.Server.FileVault.Logging;
using Serilog.Events;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Logging;

/// <summary>Tests for <see cref="LoggingBootstrap.Build"/>: level parsing, the RUST_LOG fallback, and the eager file-sink probe.</summary>
public sealed class LoggingBootstrapTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));

    public LoggingBootstrapTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void Build_DebugLevel_EnablesDebugEvents()
    {
        // Arrange
        var env = new Dictionary<string, string> { [LoggingConstants.EnvLogLevel] = "debug" };

        // Act
        using var logger = LoggingBootstrap.Build(env);

        // Assert
        Assert.True(logger.IsEnabled(LogEventLevel.Debug));
    }

    [Fact]
    public void Build_RustLogFallback_HonoredWhenPrimaryUnset()
    {
        // Arrange
        var env = new Dictionary<string, string> { [LoggingConstants.EnvLogLevelFallback] = "warn" };

        // Act
        using var logger = LoggingBootstrap.Build(env);

        // Assert
        Assert.False(logger.IsEnabled(LogEventLevel.Information));
        Assert.True(logger.IsEnabled(LogEventLevel.Warning));
    }

    [Fact]
    public void Build_GarbageLevel_DefaultsToInformation()
    {
        // Arrange
        var env = new Dictionary<string, string> { [LoggingConstants.EnvLogLevel] = "shouty-nonsense" };

        // Act
        using var logger = LoggingBootstrap.Build(env);

        // Assert
        Assert.True(logger.IsEnabled(LogEventLevel.Information));
        Assert.False(logger.IsEnabled(LogEventLevel.Debug));
    }

    [Fact]
    public void Build_UnwritableLogFilePath_FallsBackToStderrWithoutThrowing()
    {
        // Arrange
        var blockingFile = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(blockingFile, "occupied");
        var env = new Dictionary<string, string>
        {
            [LoggingConstants.EnvLogFile] = Path.Combine(blockingFile, "sub", "log.txt"),
        };

        // Act
        var exception = Record.Exception(() =>
        {
            using var logger = LoggingBootstrap.Build(env);
        });

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Build_WritableLogFilePath_ProbesAndLeavesFileOpenable()
    {
        // Arrange
        var logPath = Path.Combine(_root, "logs", "vault.log");
        var env = new Dictionary<string, string> { [LoggingConstants.EnvLogFile] = logPath };

        // Act
        using var logger = LoggingBootstrap.Build(env);

        // Assert
        Assert.True(File.Exists(logPath));
    }
}