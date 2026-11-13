using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitLsFilesToolCutTests : IDisposable
{
    private const string FirstFile = "a.txt";
    private const string SecondFile = "b.txt";
    private const string LsFilesSubcommand = "ls-files";
    private const int KeptBytesInsideTheSecondPath = 8;
    private const int KeptStageBytesInsideTheSecondPath = 108;
    private const int KeptEveryByte = 12;

    private readonly TempGitRepo _repo;

    public GitLsFilesToolCutTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(FirstFile, "one\n");
        _repo.WriteFile(SecondFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "two files");
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task LsFiles_OutputCutInsideTheSecondPath_ReturnsOnlyTheWholePath()
    {
        // Arrange
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(LsFilesSubcommand), KeptBytesInsideTheSecondPath));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        Assert.Equal([FirstFile], envelope.Results.Cast<string>());
    }

    [Fact]
    public async Task LsFilesStage_OutputCutInsideTheSecondPath_ReturnsOnlyTheWholeEntry()
    {
        // Arrange
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(LsFilesSubcommand), KeptStageBytesInsideTheSecondPath));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, stage: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var entry = Assert.IsType<LsFilesEntry>(Assert.Single(envelope.Results));
        Assert.Equal(FirstFile, entry.Path);
    }

    [Fact]
    public async Task LsFiles_OutputCutRightAfterTheLastNul_ReturnsEveryPath()
    {
        // Arrange
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(LsFilesSubcommand), KeptEveryByte));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        Assert.Equal([FirstFile, SecondFile], envelope.Results.Cast<string>());
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="runner"/>, with the real root resolver.</summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <returns>The tool.</returns>
    private static GitLsFilesTool BuildTool(IGitProcessRunner runner)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitLsFilesTool(common);
    }

    #endregion
}