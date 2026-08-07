using RaccoonNinja.McpToolset.Server.TextEdit.Logging;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Logging;

public sealed class LoggingBootstrapTests
{
    [Fact]
    public void Build_LogFile_IsUtf8WithoutBom()
    {
        // Arrange
        var parent = Path.Combine(Path.GetTempPath(), "textedit-log-bom-" + Guid.NewGuid().ToString("N"));
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
        Assert.EndsWith(LoggingConstants.DefaultLogFileName, path);
    }
}