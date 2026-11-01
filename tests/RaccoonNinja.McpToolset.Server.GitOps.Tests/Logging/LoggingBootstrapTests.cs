using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using Serilog.Core;
using Serilog.Events;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public sealed class LoggingBootstrapTests : IDisposable
{
    private const string LogFileName = "out.log";
    private const string HostLifetimeCategory = "Microsoft.Hosting.Lifetime";
    private const string ContentRootProbe = "content-root-probe";
    private const string ControlEvent = "lifetime-control";
    private const string TraceLevel = "TRACE";
    private const string McpSdkTransportCategory = "ModelContextProtocol.Server.StdioServerTransport";
    private const string McpSdkSessionCategory = "ModelContextProtocol.Server.McpServer";
    private const string McpFrameProbe = "mcp-frame-probe";
    private const string McpHandlerProbe = "mcp-handler-probe";
    private const string McpControlEvent = "mcp-sdk-control";

    private readonly string _root;
    private readonly string _logPath;

    public LoggingBootstrapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "gitops-log-" + Guid.NewGuid().ToString("N"));
        _logPath = Path.Combine(_root, LogFileName);
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
        var env = new Dictionary<string, string>
        {
            [LoggingConstants.EnvLogFile] = _logPath,
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
        Assert.True(File.Exists(_logPath));
        var content = File.ReadAllText(_logPath);
        Assert.Contains("\"event\":\"smoke\"", content);
        Assert.Contains("\"service\":\"" + LogFields.ServiceName + "\"", content);
    }

    [Fact]
    public void Build_LogFile_IsUtf8WithoutBom()
    {
        // Arrange
        var env = new Dictionary<string, string> { [LoggingConstants.EnvLogFile] = _logPath };

        // Act
        using (var logger = LoggingBootstrap.Build(env))
        {
            logger.Information("{Event}", "smoke");
        }

        // Assert
        var bytes = File.ReadAllBytes(_logPath);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.False(hasBom);
        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'{', bytes[0]);
    }

    [Fact]
    public void DefaultLogPath_ReturnsPathNextToExecutable()
    {
        // Act
        var path = LoggingBootstrap.DefaultLogPath();

        // Assert
        Assert.EndsWith("mcp-gitops.log", path);
    }

    [Fact]
    public void Build_HostLifetimeInformation_IsNotWritten()
    {
        // Arrange
        var env = new Dictionary<string, string>
        {
            [LoggingConstants.EnvLogFile] = _logPath,
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
        var content = File.ReadAllText(_logPath);
        Assert.DoesNotContain(ContentRootProbe, content, StringComparison.Ordinal);
        Assert.Contains(ControlEvent, content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TraceLevel, LogEventLevel.Verbose)]
    [InlineData("ERROR", LogEventLevel.Warning)]
    public void Build_McpSdkEventBelowItsFloor_IsNotWritten(string configuredLevel, LogEventLevel probeLevel)
    {
        // Arrange
        var env = new Dictionary<string, string>
        {
            [LoggingConstants.EnvLogFile] = _logPath,
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
        var content = File.ReadAllText(_logPath);
        Assert.DoesNotContain(McpFrameProbe, content, StringComparison.Ordinal);
        Assert.Contains(McpControlEvent, content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_McpSdkInformationAtTrace_IsWritten()
    {
        // Arrange
        var env = new Dictionary<string, string>
        {
            [LoggingConstants.EnvLogFile] = _logPath,
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
        var content = File.ReadAllText(_logPath);
        Assert.Contains(McpHandlerProbe, content, StringComparison.Ordinal);
    }
}