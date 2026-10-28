using System.Text.Json;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Repo;

public sealed class GitVersionReaderTests
{
    private const string VersionPrefix = "git version ";
    private const string MissingGitName = "mcp-gitops-tests-missing-git";
    private const string BareExecutableName = "git";

    [Theory]
    [InlineData("git version 2.55.0\n", "2.55.0")]
    [InlineData("git version 2.47.1.windows.1\r\n", "2.47.1.windows.1")]
    [InlineData("git version 2.39.5 (Apple Git-154)\n", "2.39.5 (Apple Git-154)")]
    public void ParseVersion_WithGitOutput_ReturnsVersion(string stdout, string expected)
    {
        // Act
        var version = GitVersionReader.ParseVersion(stdout);

        // Assert
        Assert.Equal(expected, version);
    }

    [Theory]
    [MemberData(nameof(UnexpectedOutputs))]
    public void ParseVersion_WithUnexpectedOutput_ReturnsNull(string stdout)
    {
        // Act
        var version = GitVersionReader.ParseVersion(stdout);

        // Assert
        Assert.Null(version);
    }

    [Fact]
    public async Task LogAsync_WithInstalledGit_LogsGitVersionLine()
    {
        // Arrange
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

        // Act
        await GitVersionReader.LogAsync(logger, TestGit.Executable, TestContext.Current.CancellationToken);

        // Assert
        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Information, logEvent.Level);
        using var line = Format(logEvent);
        Assert.Equal("git_version", line.RootElement.GetProperty(LogFields.Event).GetString());
        var version = line.RootElement.GetProperty(LogFields.GitVersion).GetString();
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Equal(version, GitVersionReader.ParseVersion(VersionPrefix + version));
    }

    [Theory]
    [MemberData(nameof(NotStartableExecutables))]
    public async Task LogAsync_WithGitNotStartable_LogsUnavailableWarning(string gitExecutable)
    {
        // Arrange
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

        // Act
        await GitVersionReader.LogAsync(logger, gitExecutable, TestContext.Current.CancellationToken);

        // Assert
        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, logEvent.Level);
        using var line = Format(logEvent);
        Assert.Equal("git_version_unavailable", line.RootElement.GetProperty(LogFields.Event).GetString());
        Assert.Equal(ErrorCodes.GitNotInstalled, line.RootElement.GetProperty(LogFields.ErrorCode).GetString());
        Assert.False(line.RootElement.TryGetProperty(LogFields.GitExitCode, out _));
    }

    #region Test Helpers

    public static TheoryData<string> NotStartableExecutables()
    {
        return new()
        {
            null,
            BareExecutableName,
            Path.Combine(Path.GetTempPath(), MissingGitName),
        };
    }

    public static TheoryData<string> UnexpectedOutputs()
    {
        return new()
        {
            string.Empty,
            "usage: git [-v | --version]\n",
            "git version \n",
            "git version 2.55.0\u001b\n",
            VersionPrefix + new string('9', GitVersionReader.MaxVersionLength + 1),
        };
    }

    private static JsonDocument Format(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        new AllowlistJsonFormatter().Format(logEvent, writer);
        return JsonDocument.Parse(writer.ToString());
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events => [.. _events];

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);
    }

    #endregion
}