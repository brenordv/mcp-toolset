using RaccoonNinja.McpToolset.Cli.SkillStats.Tests.TestSupport;
using RaccoonNinja.McpToolset.Common.SkillStats.Configuration;

namespace RaccoonNinja.McpToolset.Cli.SkillStats.Tests.Ingest;

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
        Assert.Contains("unrecognized_shape", log);
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
    public void Run_MalformedJson_ExitsOneWithLogAndStderr()
    {
        // Arrange
        using var harness = new IngestHarness();

        // Act
        var (exitCode, stderr) = harness.Run("{ not json");

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(harness.Rows());
        Assert.StartsWith("skill-stats-cli: failed to record", stderr);
        Assert.Contains("ingest-errors.log", stderr);
        Assert.Contains("parse", harness.ReadErrorLog());
    }

    [Fact]
    public void Run_EmptyStdin_ExitsOneWithStderr()
    {
        // Arrange
        using var harness = new IngestHarness();

        // Act
        var (exitCode, stderr) = harness.Run(string.Empty);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(harness.Rows());
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public void Run_ConfigLoaderThrows_ExitsOneAndWritesNoLog()
    {
        // Arrange
        using var harness = new IngestHarness();
        Func<SkillStatsConfig> throwingLoader = () => throw new SkillStatsStartupException("no home");

        // Act
        var (exitCode, stderr) = harness.Run("{\"tool_input\":{\"skill\":\"csharp\"}}", throwingLoader);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.StartsWith("skill-stats-cli: failed to record", stderr);
        Assert.False(harness.ErrorLogExists());
    }

    [Fact]
    public void Run_StorePathUnopenable_ExitsOneWithLogAndStderr()
    {
        // Arrange
        using var harness = new IngestHarness();
        Directory.CreateDirectory(harness.Home);
        Directory.CreateDirectory(harness.Config.DbPath);

        // Act
        var (exitCode, stderr) = harness.Run("{\"tool_input\":{\"skill\":\"csharp\"}}");

        // Assert
        Assert.Equal(1, exitCode);
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
        Assert.Equal(1, exitCode);
        Assert.True(harness.RotatedLogExists());
    }

    [Fact]
    public void Run_UserPromptExpansionPayload_InsertsRowFromCommandFields()
    {
        // Arrange
        using var harness = new IngestHarness();
        const string payload = "{\"session_id\":\"s1\",\"cwd\":\"/work\",\"hook_event_name\":\"UserPromptExpansion\","
            + "\"expansion_type\":\"slash_command\",\"command_name\":\"csharp\",\"command_args\":\"do the thing\","
            + "\"command_source\":\"skill\",\"prompt\":\"/csharp do the thing\"}";

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
    }

    [Fact]
    public void Run_UserPromptExpansionPayloadWithoutArgs_StoresNullArgsNotPrompt()
    {
        // Arrange
        using var harness = new IngestHarness();
        const string payload = "{\"session_id\":\"s1\",\"hook_event_name\":\"UserPromptExpansion\","
            + "\"command_name\":\"csharp\",\"prompt\":\"/csharp SECRET_PROMPT_MARKER\"}";

        // Act
        var (exitCode, _) = harness.Run(payload);

        // Assert
        Assert.Equal(0, exitCode);
        var row = Assert.Single(harness.Rows());
        Assert.Equal("csharp", row.Skill);
        Assert.Null(row.Args);
    }

    [Fact]
    public void Run_PayloadWithBothShapes_PrefersToolInput()
    {
        // Arrange
        using var harness = new IngestHarness();
        const string payload = "{\"tool_input\":{\"skill\":\"from-tool\",\"args\":\"tool args\"},"
            + "\"command_name\":\"from-root\",\"command_args\":\"root args\"}";

        // Act
        var (exitCode, _) = harness.Run(payload);

        // Assert
        Assert.Equal(0, exitCode);
        var row = Assert.Single(harness.Rows());
        Assert.Equal("from-tool", row.Skill);
        Assert.Equal("tool args", row.Args);
    }

    [Fact]
    public void Run_UnrecognizedShape_ExitsZeroAndLogsRootKeyNamesNotValues()
    {
        // Arrange
        using var harness = new IngestHarness();
        const string payload = "{\"session_id\":\"s1\",\"foo\":\"SECRET_VALUE_MARKER\"}";

        // Act
        var (exitCode, stderr) = harness.Run(payload);
        var log = harness.ReadErrorLog();

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(harness.Rows());
        Assert.Empty(stderr);
        Assert.Contains("skipped reason=unrecognized_shape root_keys=[session_id,foo]", log);
        Assert.DoesNotContain("SECRET_VALUE_MARKER", log);
    }

    [Fact]
    public void Run_SlashOnlyCommandName_SkipsAsNoSkill()
    {
        // Arrange
        using var harness = new IngestHarness();

        // Act
        var (exitCode, _) = harness.Run("{\"command_name\":\"/\"}");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(harness.Rows());
        Assert.Contains("skipped reason=no_skill", harness.ReadErrorLog());
    }

    [Fact]
    public void Run_NonObjectPayload_SkipsAsUnrecognizedShape()
    {
        // Arrange
        using var harness = new IngestHarness();

        // Act
        var (exitCode, stderr) = harness.Run("[\"csharp\"]");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(harness.Rows());
        Assert.Empty(stderr);
        Assert.Contains("skipped reason=unrecognized_shape", harness.ReadErrorLog());
    }
}