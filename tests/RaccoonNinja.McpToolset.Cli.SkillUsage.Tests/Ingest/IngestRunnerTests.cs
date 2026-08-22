using RaccoonNinja.McpToolset.Cli.SkillUsage.Tests.TestSupport;
using RaccoonNinja.McpToolset.Shared.SkillStats.Configuration;

namespace RaccoonNinja.McpToolset.Cli.SkillUsage.Tests.Ingest;

public sealed class IngestRunnerTests
{
    [Fact]
    public void Run_PrimaryShapePayload_InsertsRowAndExitsZero()
    {
        // Arrange
        using var harness = new IngestHarness();
        const string payload = "{\"session_id\":\"s1\",\"cwd\":\"/work\",\"tool_input\":{\"skill\":\"csharp\",\"args\":\"do the thing\"}}";

        // Act
        var (exitCode, stderr) = harness.Run(payload);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        var row = Assert.Single(harness.Rows());
        Assert.Equal("csharp", row.Skill);
        Assert.Equal("do the thing", row.Args);
        Assert.Equal("s1", row.SessionId);
        Assert.Equal("/work", row.Cwd);
        Assert.True(row.UsedAt > 0);
    }

    [Fact]
    public void Run_DocsDriftShapePayload_InsertsWithCompactJsonArgs()
    {
        // Arrange
        using var harness = new IngestHarness();
        const string payload = "{\"tool_input\":{\"name\":\"csharp\",\"input\":{\"a\":1,\"b\":2}}}";

        // Act
        var (exitCode, stderr) = harness.Run(payload);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        var row = Assert.Single(harness.Rows());
        Assert.Equal("csharp", row.Skill);
        Assert.Equal("{\"a\":1,\"b\":2}", row.Args);
    }

    [Fact]
    public void Run_LeadingSlashSkill_StoredNormalized()
    {
        // Arrange
        using var harness = new IngestHarness();
        const string payload = "{\"tool_input\":{\"skill\":\"/csharp\"}}";

        // Act
        var (exitCode, _) = harness.Run(payload);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("csharp", Assert.Single(harness.Rows()).Skill);
    }

    [Fact]
    public void Run_NoSkillName_ExitsZeroAndLogsKeyNamesNotValues()
    {
        // Arrange
        using var harness = new IngestHarness();
        const string payload = "{\"tool_input\":{\"foo\":\"SECRET_VALUE_MARKER\",\"bar\":123}}";

        // Act
        var (exitCode, stderr) = harness.Run(payload);
        var log = harness.ReadErrorLog();

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(harness.Rows());
        Assert.Empty(stderr);
        Assert.Contains("skipped", log);
        Assert.Contains("foo", log);
        Assert.Contains("bar", log);
        Assert.DoesNotContain("SECRET_VALUE_MARKER", log);
    }

    [Fact]
    public void Run_NoSkillHostileKeyName_SanitizesAndCapsKeysInLog()
    {
        // Arrange
        using var harness = new IngestHarness();
        var longKey = new string('k', 200);
        var payload = "{\"tool_input\":{\"a\\u0007b\":1,\"" + longKey + "\":2}}";

        // Act
        var (exitCode, _) = harness.Run(payload);
        var log = harness.ReadErrorLog();

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("a?b", log);
        Assert.Contains(new string('k', 64), log);
        Assert.DoesNotContain(longKey, log);
    }

    [Fact]
    public void Run_OversizeStdin_ExitsZeroAndSkipsAsOversize()
    {
        // Arrange
        using var harness = new IngestHarness();
        var oversized = new byte[(4 * 1024 * 1024) + 1024];
        Array.Fill(oversized, (byte)'x');

        // Act
        var (exitCode, stderr) = harness.RunBytes(oversized);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(harness.Rows());
        Assert.Empty(stderr);
        Assert.Contains("oversize", harness.ReadErrorLog());
    }

    [Fact]
    public void Run_MalformedJson_ExitsTwoWithLogAndStderr()
    {
        // Arrange
        using var harness = new IngestHarness();

        // Act
        var (exitCode, stderr) = harness.Run("{ not json");

        // Assert
        Assert.Equal(2, exitCode);
        Assert.Empty(harness.Rows());
        Assert.Contains("ingest-errors.log", stderr);
        Assert.Contains("parse", harness.ReadErrorLog());
    }

    [Fact]
    public void Run_EmptyStdin_ExitsTwo()
    {
        // Arrange
        using var harness = new IngestHarness();

        // Act
        var (exitCode, stderr) = harness.Run(string.Empty);

        // Assert
        Assert.Equal(2, exitCode);
        Assert.Empty(harness.Rows());
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public void Run_ConfigLoaderThrows_ExitsTwoAndWritesNoLog()
    {
        // Arrange
        using var harness = new IngestHarness();
        Func<SkillStatsConfig> throwingLoader = () => throw new SkillStatsStartupException("no home");

        // Act
        var (exitCode, stderr) = harness.Run("{\"tool_input\":{\"skill\":\"csharp\"}}", throwingLoader);

        // Assert
        Assert.Equal(2, exitCode);
        Assert.Contains("failed to record", stderr);
        Assert.False(harness.ErrorLogExists());
    }

    [Fact]
    public void Run_StorePathUnopenable_ExitsTwoWithLogAndStderr()
    {
        // Arrange
        using var harness = new IngestHarness();
        Directory.CreateDirectory(harness.Home);
        Directory.CreateDirectory(harness.Config.DbPath);

        // Act
        var (exitCode, stderr) = harness.Run("{\"tool_input\":{\"skill\":\"csharp\"}}");

        // Assert
        Assert.Equal(2, exitCode);
        Assert.Contains("ingest-errors.log", stderr);
        Assert.Contains("open-store", harness.ReadErrorLog());
    }

    [Fact]
    public void Run_OversizedErrorLog_RotatesToOldBeforeAppend()
    {
        // Arrange
        using var harness = new IngestHarness();
        Directory.CreateDirectory(harness.Home);
        File.WriteAllBytes(harness.Config.ErrorLogPath, new byte[6 * 1024 * 1024]);

        // Act
        var (exitCode, _) = harness.Run("{ not json");

        // Assert
        Assert.Equal(2, exitCode);
        Assert.True(harness.RotatedLogExists());
    }
}