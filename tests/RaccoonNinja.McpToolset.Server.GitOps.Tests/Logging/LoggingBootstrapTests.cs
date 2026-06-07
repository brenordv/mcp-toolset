using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class LoggingBootstrapTests
{
    [Fact]
    public void Build_With_Empty_Env_Uses_Default_Path_Or_Falls_Back_To_Stderr()
    {
        using var logger = LoggingBootstrap.Build(new Dictionary<string, string>());
        logger.Information("{Event}", "smoke");
    }

    [Fact]
    public void Build_With_Bad_Log_File_Falls_Back_To_Stderr_Without_Throwing()
    {
        var env = new Dictionary<string, string>
        {
            [LoggingConstants.EnvLogFile] = "definitely-not-absolute.log",
        };
        using var logger = LoggingBootstrap.Build(env);
        logger.Warning("{Event}", "after_rejection");
    }

    [Fact]
    public void Build_With_Valid_Path_Writes_To_File()
    {
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
            using (var logger = LoggingBootstrap.Build(env))
            {
                logger
                    .ForContext(LogFields.Event, "smoke")
                    .ForContext(LogFields.Tool, "test")
                    .Information("smoke");
            }
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
    public void DefaultLogPath_Returns_Path_Next_To_Executable()
    {
        var path = LoggingBootstrap.DefaultLogPath();
        Assert.EndsWith("mcp-gitops.log", path);
    }
}