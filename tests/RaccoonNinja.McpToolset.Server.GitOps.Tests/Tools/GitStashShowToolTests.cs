using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitStashShowToolTests : IDisposable
{
    private const string ChangedFile = "a.txt";
    private const string SubmodulePath = "sub";
    private const string PatchFlag = "-p";
    private const string NumstatFlag = "--numstat";

    private readonly TempGitRepo _repo;
    private readonly GitStashShowTool _tool;

    public GitStashShowToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(ChangedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");
        _repo.WriteFile(ChangedFile, "one\ntwo\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "second");
        _repo.WriteFile(ChangedFile, "one\ntwo\nthree\n");
        _repo.Run("stash", "push", "-m", "wip");

        _tool = BuildTool(new GitProcessRunner(NullLogger<GitProcessRunner>.Instance), NullLoggerFactory.Instance);
    }

    public void Dispose() => _repo.Dispose();

    [Theory]
    [InlineData("color.ui", "always")]
    [InlineData("diff.noprefix", "true")]
    [InlineData("diff.dstPrefix", "x/")]
    public async Task StashShow_WithPatchShapeConfig_ReturnsChangedFile(string configKey, string configValue)
    {
        // Arrange
        _repo.Run("config", configKey, configValue);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, index: 0, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(ChangedFile, file.Path);
        Assert.Equal(1, file.Additions);
        Assert.NotEmpty(file.Hunks);
    }

    [Fact]
    public async Task StashShow_WithGitmodulesIgnoreAll_ReturnsSubmoduleEntry()
    {
        // Arrange
        _repo.StageIgnoredSubmoduleMove(SubmodulePath);
        _repo.WriteFile(ChangedFile, "four\n");
        _repo.Run("stash", "push", "-m", "move submodule");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, index: 0, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files, entry => entry.Path == SubmodulePath);
        Assert.Equal(1, file.Additions);
        Assert.Equal(1, file.Deletions);
    }

    [Fact]
    public async Task StashShow_PatchPassYieldsNoFile_LogsPatchParseEmptyWarning()
    {
        // Arrange
        var loggerFactory = new RecordingLoggerFactory();
        var tool = BuildTool(new StdoutDroppingRunner(argv => argv.Contains(PatchFlag)), loggerFactory);

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, index: 0, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var warning = Assert.Single(loggerFactory.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal("patch_parse_empty", warning.Message);
    }

    [Fact]
    public async Task StashShow_PatchFileMissingFromNumstat_LogsPatchPathUnmatchedWarning()
    {
        // Arrange
        var loggerFactory = new RecordingLoggerFactory();
        var tool = BuildTool(new StdoutDroppingRunner(argv => argv.Contains(NumstatFlag)), loggerFactory);

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, index: 0, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var warning = Assert.Single(loggerFactory.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal("patch_path_unmatched", warning.Message);
    }

    #region Test Helpers

    /// <summary>
    /// Build the tool under test over <paramref name="runner"/>, logging through <paramref name="loggerFactory"/>, with the
    /// real root resolver.
    /// </summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <param name="loggerFactory">The factory the call's log lines go to.</param>
    /// <returns>The tool.</returns>
    private static GitStashShowTool BuildTool(IGitProcessRunner runner, ILoggerFactory loggerFactory)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            loggerFactory,
            TestGit.Executable);
        return new GitStashShowTool(common);
    }

    #endregion
}