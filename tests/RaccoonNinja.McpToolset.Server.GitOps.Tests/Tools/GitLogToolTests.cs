using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitLogToolTests : IDisposable
{
    private const string AlphaFile = "alpha.txt";
    private const string AlphaSubject = "GMS-1 alpha";
    private const string BetaFile = "beta.txt";
    private const string BetaSubject = "DEVOPS-2 beta";
    private const string FeatureSubject = "GMS-9 feature work";
    private const string AlternationPattern = "GMS-1|DEVOPS-2";
    private const string SignedSubject = "GMS-4 signed";
    private const string SubmodulePath = "sub";
    private const string SubmoduleMoveSubject = "move submodule";
    private const string RenamedFile = "renamed.txt";
    private const string RenameSubject = "rename beta";

    private readonly TempGitRepo _repo;
    private readonly GitLogTool _tool;

    public GitLogToolTests()
    {
        _repo = new TempGitRepo();
        CommitFile(AlphaFile, AlphaSubject);
        CommitFile(BetaFile, BetaSubject);
        CommitFile("gamma.txt", "plain gamma");
        _repo.Run("checkout", "-b", "feature");
        CommitFile("feature.txt", FeatureSubject);
        _repo.Run("checkout", "main");

        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        _tool = new GitLogTool(common, new RefVerifier(TestGit.Executable));
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Log_WithPerlRegexAlternation_ReturnsMatchingCommits()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, grep: AlternationPattern, perlRegex: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(2, envelope.Count);
        Assert.Equal([BetaSubject, AlphaSubject], Subjects(envelope));
        Assert.True((bool)envelope.FiltersApplied["perl_regex"]);
    }

    [Fact]
    public async Task Log_WithPerlRegexCharacterClass_ReturnsMatchingCommit()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, grep: "DEVOPS-\\d", perlRegex: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(1, envelope.Count);
        Assert.Equal([BetaSubject], Subjects(envelope));
    }

    [Fact]
    public async Task Log_WithoutPerlRegex_TreatsPipeAsLiteral()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, grep: AlternationPattern, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(0, envelope.Count);
    }

    [Fact]
    public async Task Log_WithExtendedPatternTypeInRepoConfig_StillTreatsPipeAsLiteral()
    {
        // Arrange
        _repo.Run("config", "grep.patternType", "extended");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, grep: AlternationPattern, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(0, envelope.Count);
    }

    [Fact]
    public async Task Log_Default_ListsOnlyTheCurrentBranch()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(3, envelope.Count);
        Assert.DoesNotContain(FeatureSubject, Subjects(envelope));
    }

    [Fact]
    public async Task Log_WithAllRefs_IncludesCommitsOnOtherBranches()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, allRefs: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(4, envelope.Count);
        Assert.Contains(FeatureSubject, Subjects(envelope));
        Assert.True((bool)envelope.FiltersApplied["all_refs"]);
    }

    [Fact]
    public async Task Log_WithAllRefsAndGrep_FindsCommitOnAnotherBranch()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, grep: "GMS-9", allRefs: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(1, envelope.Count);
        Assert.Equal([FeatureSubject], Subjects(envelope));
    }

    [Fact]
    public async Task Log_WithAllRefsAndPerlRegex_FindsAcrossBranchesByPattern()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, grep: "GMS-\\d", perlRegex: true, allRefs: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(2, envelope.Count);
        Assert.Equal([AlphaSubject, FeatureSubject], Subjects(envelope));
    }

    [Fact]
    public async Task Log_WithAllRefsAndRef_IsRejected()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: "main", allRefs: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("all_refs", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task Log_WithShowSignatureConfigAndSignedCommit_DoesNotRunSignatureProgram()
    {
        // Arrange
        var signedHash = _repo.CommitWithForgedSignature(SignedSubject);
        var marker = _repo.ConfigureSignatureProgramTrap();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, maxCount: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var commit = Assert.IsType<Commit>(Assert.Single(envelope.Results));
        Assert.Equal(signedHash, commit.Hash);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Log_PickaxeOnGitlinkWithGitmodulesIgnoreAll_ReturnsMovingCommit()
    {
        // Arrange
        var movedTo = _repo.StageIgnoredSubmoduleMove(SubmodulePath);
        _repo.Run("commit", "-m", SubmoduleMoveSubject);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, pickaxe: movedTo, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var commit = Assert.IsType<Commit>(Assert.Single(envelope.Results));
        Assert.Equal(SubmoduleMoveSubject, commit.Subject);
    }

    [Fact]
    public async Task Log_FollowOnGitlinkWithGitmodulesIgnoreAll_ReturnsMovingCommit()
    {
        // Arrange
        _repo.StageIgnoredSubmoduleMove(SubmodulePath);
        _repo.Run("commit", "-m", SubmoduleMoveSubject);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, paths: [SubmodulePath], follow: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Contains(SubmoduleMoveSubject, Subjects(envelope));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task Log_SinglePathWithLogFollowConfig_FollowsRenamesOnlyWhenAsked(bool follow, int expectedCount)
    {
        // Arrange
        _repo.Run("mv", BetaFile, RenamedFile);
        _repo.Run("commit", "-m", RenameSubject);
        _repo.Run("config", "log.follow", "true");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, paths: [RenamedFile], follow: follow, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(expectedCount, envelope.Count);
        Assert.Contains(RenameSubject, Subjects(envelope));
    }

    [Fact]
    public async Task Log_PickaxeOnRootCommitWithShowRootOff_ReturnsRootCommit()
    {
        // Arrange
        var rootCommit = _repo.ConfigureShowRootOff();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, pickaxe: AlphaFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var commit = Assert.IsType<Commit>(Assert.Single(envelope.Results));
        Assert.Equal(rootCommit, commit.Hash);
    }

    [Fact]
    public async Task Log_FollowOnRootCommitFileWithShowRootOff_ReturnsRootCommit()
    {
        // Arrange
        var rootCommit = _repo.ConfigureShowRootOff();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, paths: [AlphaFile], follow: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var commit = Assert.IsType<Commit>(Assert.Single(envelope.Results));
        Assert.Equal(rootCommit, commit.Hash);
    }

    #region Test Helpers

    private void CommitFile(string path, string message)
    {
        _repo.WriteFile(path, $"{path}\n");
        _repo.Run("add", path);
        _repo.Run("commit", "-m", message);
    }

    private static List<string> Subjects(ResultEnvelope envelope)
        => envelope.Results
            .Cast<Commit>()
            .Select(commit => commit.Subject)
            .Order(StringComparer.Ordinal)
            .ToList();

    #endregion
}