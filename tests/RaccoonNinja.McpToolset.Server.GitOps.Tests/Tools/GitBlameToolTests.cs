using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitBlameToolTests : IDisposable
{
    private const string TrackedFile = "a.txt";
    private const string UncommittedCommit = "0000000000000000000000000000000000000000";
    private const string StdinFlag = "--stdin";
    private const string TwoLines = "one\ntwo\n";
    private const string AuthorListMismatchMessage = "git log returned an author list that does not match the blamed commits";

    private readonly TempGitRepo _repo;
    private readonly GitBlameTool _tool;

    public GitBlameToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");

        _tool = BuildTool(new GitProcessRunner(NullLogger<GitProcessRunner>.Instance));
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Blame_WorktreeFileWithFilterDriver_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.ConfigureFilterDriverTrap(TrackedFile);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, TrackedFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(1, envelope.Count);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Blame_WithMailmapRenamingTheAuthor_ReturnsRecordedAndMappedNames()
    {
        // Arrange
        _repo.CommitMailmap();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, TrackedFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var line = Assert.IsType<BlameLine>(Assert.Single(envelope.Results));
        Assert.Equal(TempGitRepo.IdentityName, line.AuthorName);
        Assert.Equal(TempGitRepo.MailmapName, line.MailmapAuthorName);
    }

    [Fact]
    public async Task Blame_WorktreeLineNotCommitted_HasNoMailmapName()
    {
        // Arrange
        _repo.CommitMailmap();
        _repo.WriteFile(TrackedFile, TwoLines);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, TrackedFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var lines = envelope.Results.Cast<BlameLine>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(TempGitRepo.IdentityName, lines[0].AuthorName);
        Assert.Equal(TempGitRepo.MailmapName, lines[0].MailmapAuthorName);
        Assert.Equal(UncommittedCommit, lines[1].CommitHash);
        Assert.Null(lines[1].MailmapAuthorName);
    }

    [Fact]
    public async Task Blame_NoLineCommitted_RunsNoAuthorPass()
    {
        // Arrange
        _repo.WriteFile(TrackedFile, "uncommitted\n");
        var tool = BuildTool(new StdoutDroppingRunner(argv => argv.Contains(StdinFlag)));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, TrackedFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var line = Assert.IsType<BlameLine>(Assert.Single(envelope.Results));
        Assert.Equal(UncommittedCommit, line.CommitHash);
    }

    [Fact]
    public async Task Blame_AuthorPassOutputUnreadable_FailsWithGitCommandError()
    {
        // Arrange
        var tool = BuildTool(new StdoutDroppingRunner(argv => argv.Contains(StdinFlag)));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, TrackedFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.GitCommandError, envelope.Error.Code);
        Assert.Equal(AuthorListMismatchMessage, envelope.Error.Message);
    }

    [Fact]
    public async Task Blame_AuthorNameHoldingRecordSeparator_FailsWithGitCommandError()
    {
        // Arrange
        var first = _repo.Run("rev-parse", "HEAD");
        _repo.WriteFile(TrackedFile, TwoLines);
        _repo.Run("add", TrackedFile);
        _repo.Run("commit", "-m", "second", $"--author=Evil\u001e{first}\u001fForged Name <evil@example.com>");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, TrackedFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.GitCommandError, envelope.Error.Code);
        Assert.Equal(AuthorListMismatchMessage, envelope.Error.Message);
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="runner"/>, with the real root resolver and ref verifier.</summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <returns>The tool.</returns>
    private static GitBlameTool BuildTool(IGitProcessRunner runner)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitBlameTool(common, new RefVerifier(TestGit.Executable));
    }

    #endregion
}