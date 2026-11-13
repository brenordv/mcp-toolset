using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitGrepToolFramingTests : IDisposable
{
    private const string Pattern = "hit";
    private const string HitLine = "hit\n";
    private const string FirstFile = "a.txt";
    private const string SecondFile = "b.txt";
    private const int SecondFileHits = 12;
    private const int AllHits = SecondFileHits + 1;
    private const string BinaryFile = "bin.dat";
    private const string NewlineEntryName = "x\nREADME.md";
    private const string NeedlePattern = "needle";
    private const string NeedleLine = "needle in a haystack";
    private const string HeadRef = "HEAD";
    private const string GrepSubcommand = "grep";
    private const int KeptBytesInsideTheSecondPath = 15;
    private const int KeptBytesInsideTheSecondCount = 15;

    private readonly TempGitRepo _repo;
    private readonly GitGrepTool _tool;

    public GitGrepToolFramingTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(FirstFile, HitLine);
        _repo.WriteFile(SecondFile, string.Concat(Enumerable.Repeat(HitLine, SecondFileHits)));
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "add grep tree");

        _tool = BuildTool(new GitProcessRunner(NullLogger<GitProcessRunner>.Instance));
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Grep_AtRefWithPathHoldingNewline_ReturnsTheWholePath()
    {
        // Arrange
        _repo.CommitTreeWithEntryName(NewlineEntryName, NeedleLine + "\n");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, NeedlePattern, @ref: HeadRef, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var match = Assert.IsType<GrepMatch>(Assert.Single(envelope.Results));
        Assert.Equal(NewlineEntryName, match.Path);
        Assert.Equal(1, match.Line);
        Assert.Equal(NeedleLine, match.Content);
    }

    [Fact]
    public async Task Grep_CountOnlyAtRefWithPathHoldingNewline_ReturnsTheWholePath()
    {
        // Arrange
        _repo.CommitTreeWithEntryName(NewlineEntryName, NeedleLine + "\n");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, NeedlePattern, @ref: HeadRef, countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var count = Assert.IsType<GrepFileCount>(Assert.Single(envelope.Results));
        Assert.Equal(new GrepFileCount { Path = NewlineEntryName, MatchCount = 1 }, count);
    }

    [Fact]
    public async Task Grep_WithGrepColumnConfig_ReturnsTheLineWithoutAColumn()
    {
        // Arrange
        _repo.ConfigureGrepColumnOn(Pattern, FirstFile);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(AllHits, envelope.Count);
        Assert.All(envelope.Results.Cast<GrepMatch>(), match => Assert.Equal(Pattern, match.Content));
    }

    [Fact]
    public async Task Grep_BinaryFileHoldingThePattern_LeavesItOut()
    {
        // Arrange
        _repo.CommitBinaryFileHolding(BinaryFile, Pattern);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(AllHits, envelope.Count);
        Assert.DoesNotContain(envelope.Results.Cast<GrepMatch>(), match => match.Path == BinaryFile);
    }

    [Fact]
    public async Task Grep_CountOnlyBinaryFileHoldingThePattern_CountsIt()
    {
        // Arrange
        _repo.CommitBinaryFileHolding(BinaryFile, Pattern);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Contains(new GrepFileCount { Path = BinaryFile, MatchCount = 1 }, envelope.Results.Cast<GrepFileCount>());
    }

    [Fact]
    public async Task Grep_OutputCutInsideTheSecondPath_ReturnsOnlyTheWholeMatch()
    {
        // Arrange
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(GrepSubcommand), KeptBytesInsideTheSecondPath));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, Pattern, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var match = Assert.IsType<GrepMatch>(Assert.Single(envelope.Results));
        Assert.Equal(FirstFile, match.Path);
        Assert.Equal(1, match.Line);
        Assert.Equal(Pattern, match.Content);
    }

    [Fact]
    public async Task Grep_CountOnlyOutputCutInsideTheSecondCount_LeavesThatCountOut()
    {
        // Arrange
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(GrepSubcommand), KeptBytesInsideTheSecondCount));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, Pattern, countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var count = Assert.IsType<GrepFileCount>(Assert.Single(envelope.Results));
        Assert.Equal(new GrepFileCount { Path = FirstFile, MatchCount = 1 }, count);
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="runner"/>, with the real root resolver and ref verifier.</summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <returns>The tool.</returns>
    private static GitGrepTool BuildTool(IGitProcessRunner runner)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitGrepTool(common, new RefVerifier(TestGit.Executable));
    }

    #endregion
}