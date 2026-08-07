using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class LoggingBootstrapTests
{
    [Fact]
    public void Build_WithEmptyEnvUsesDefaultPathOrFallsBackToStderr()
    {
        // Act & Assert
        using var logger = LoggingBootstrap.Build(new Dictionary<string, string>());
        logger.Information("{Event}", "smoke");
    }

    [Fact]
    public void Build_WithBadLogFileFallsBackToStderrWithoutThrowing()
    {
        // Arrange
        var env = new Dictionary<string, string>
        {
            [LoggingConstants.EnvLogFile] = "definitely-not-absolute.log",
        };

        // Act & Assert
        using var logger = LoggingBootstrap.Build(env);
        logger.Warning("{Event}", "after_rejection");
    }

    [Fact]
    public void Build_WithValidPathWritesToFile()
    {
        // Arrange
        var parent = Path.Combine(Path.GetTempPath(), "log-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var logPath = Path.Combine(parent, "out.log");
        try
        {
            var env = new Dictionary<string, string>
            {
                [LoggingConstants.EnvLogFile] = logPath,
                [LoggingConstants.EnvLogLevel] = "DEBUG",
            };

            // Act
            using (var logger = LoggingBootstrap.Build(env))
            {
                logger
                    .ForContext(LogFields.Event, "smoke")
                    .ForContext(LogFields.Tool, "test")
                    .Information("smoke");
            }

            // Assert
            Assert.True(File.Exists(logPath));
            var content = File.ReadAllText(logPath);
            Assert.Contains("\"event\":\"smoke\"", content);
            Assert.Contains("\"service\":\"" + LogFields.ServiceName + "\"", content);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void Build_LogFile_IsUtf8WithoutBom()
    {
        // Arrange
        var parent = Path.Combine(Path.GetTempPath(), "log-bom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var logPath = Path.Combine(parent, "out.log");
        try
        {
            var env = new Dictionary<string, string> { [LoggingConstants.EnvLogFile] = logPath };

            // Act
            using (var logger = LoggingBootstrap.Build(env))
            {
                logger.Information("{Event}", "smoke");
            }

            // Assert
            var bytes = File.ReadAllBytes(logPath);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.False(hasBom);
            Assert.NotEmpty(bytes);
            Assert.Equal((byte)'{', bytes[0]);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void DefaultLogPath_ReturnsPathNextToExecutable()
    {
        // Act
        var path = LoggingBootstrap.DefaultLogPath();

        // Assert
        Assert.EndsWith("mcp-gitops.log", path);
    }
}