using RaccoonNinja.McpToolset.Server.SkillStats.Logging;
using Serilog.Core;
using Serilog.Events;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.Logging;

public sealed class LoggingBootstrapTests
{
    private const string HostLifetimeCategory = "Microsoft.Hosting.Lifetime";
    private const string ContentRootProbe = "content-root-probe";
    private const string ControlEvent = "lifetime-control";
    private const string McpSdkFolderPrefix = "skillstats-log-mcp-sdk-";
    private const string TraceLevel = "TRACE";
    private const string McpSdkTransportCategory = "ModelContextProtocol.Server.StdioServerTransport";
    private const string McpSdkSessionCategory = "ModelContextProtocol.Server.McpServer";
    private const string McpFrameProbe = "mcp-frame-probe";
    private const string McpHandlerProbe = "mcp-handler-probe";
    private const string McpControlEvent = "mcp-sdk-control";

    [Fact]
    public void Build_LogFile_IsUtf8WithoutBom()
    {
        // Arrange
        var parent = Path.Combine(Path.GetTempPath(), "skillstats-log-bom-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Build_HostLifetimeInformation_IsNotWritten()
    {
        // Arrange
        var parent = Path.Combine(Path.GetTempPath(), "skillstats-log-lifetime-" + Guid.NewGuid().ToString("N"));
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
                    .ForContext(Constants.SourceContextPropertyName, HostLifetimeCategory)
                    .Information("Content root path: {ContentRoot}", ContentRootProbe);
                logger.Information("{Event}", ControlEvent);
            }

            // Assert
            var content = File.ReadAllText(logPath);
            Assert.DoesNotContain(ContentRootProbe, content, StringComparison.Ordinal);
            Assert.Contains(ControlEvent, content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Theory]
    [InlineData(TraceLevel, LogEventLevel.Verbose)]
    [InlineData("ERROR", LogEventLevel.Warning)]
    public void Build_McpSdkEventBelowItsFloor_IsNotWritten(string configuredLevel, LogEventLevel probeLevel)
    {
        // Arrange
        var parent = Path.Combine(Path.GetTempPath(), McpSdkFolderPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var logPath = Path.Combine(parent, "out.log");
        try
        {
            var env = new Dictionary<string, string>
            {
                [LoggingConstants.EnvLogFile] = logPath,
                [LoggingConstants.EnvLogLevel] = configuredLevel,
            };

            // Act
            using (var logger = LoggingBootstrap.Build(env))
            {
                logger
                    .ForContext(Constants.SourceContextPropertyName, McpSdkTransportCategory)
                    .Write(probeLevel, "transport received message. Message: '{Message}'.", McpFrameProbe);
                logger.Error("{Event}", McpControlEvent);
            }

            // Assert
            var content = File.ReadAllText(logPath);
            Assert.DoesNotContain(McpFrameProbe, content, StringComparison.Ordinal);
            Assert.Contains(McpControlEvent, content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void Build_McpSdkInformationAtTrace_IsWritten()
    {
        // Arrange
        var parent = Path.Combine(Path.GetTempPath(), McpSdkFolderPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var logPath = Path.Combine(parent, "out.log");
        try
        {
            var env = new Dictionary<string, string>
            {
                [LoggingConstants.EnvLogFile] = logPath,
                [LoggingConstants.EnvLogLevel] = TraceLevel,
            };

            // Act
            using (var logger = LoggingBootstrap.Build(env))
            {
                logger
                    .ForContext(Constants.SourceContextPropertyName, McpSdkSessionCategory)
                    .Information("method '{Method}' request handler called.", McpHandlerProbe);
            }

            // Assert
            var content = File.ReadAllText(logPath);
            Assert.Contains(McpHandlerProbe, content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }
}