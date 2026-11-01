using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitStashListToolTests : IDisposable
{
    private const string TrackedFile = "a.txt";
    private const string SignedSubject = "signed";

    private readonly TempGitRepo _repo;
    private readonly GitStashListTool _tool;

    public GitStashListToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");

        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        _tool = new GitStashListTool(common);
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task StashList_WithShowSignatureConfigAndSignedStashCommit_DoesNotRunSignatureProgram()
    {
        // Arrange
        var signedHash = _repo.CommitWithForgedSignature(SignedSubject);
        _repo.Run("update-ref", "--create-reflog", "-m", SignedSubject, "refs/stash", signedHash);
        var marker = _repo.ConfigureSignatureProgramTrap();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var entry = Assert.IsType<StashEntry>(Assert.Single(envelope.Results));
        Assert.Equal(SignedSubject, entry.Subject);
        Assert.False(File.Exists(marker));
    }
}