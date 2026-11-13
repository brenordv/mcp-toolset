using System.Diagnostics;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Repo;

public sealed class RepoRootResolverTests : IDisposable
{
    private const string WorkTreeElsewhereMessage = "the work tree git reports for cwd leads to another repository or to none";
    private const string TrackedFile = "a.txt";
    private const string NestedRepository = "inner";
    private const string Subdirectory = "src";
    private const string SubmodulePath = "sub";
    private const string SubmoduleFile = "s.txt";
    private const string LinkedWorktreePath = "wt";
    private const string LinkedWorktreeBranch = "wt-branch";
    private const string LinkName = "repo-link";
    private const string LinkParentPrefix = "mcp-gitops-link-";
    private const string SymbolicLinkSkipReason = "creating a directory symbolic link is not permitted in this environment";
    private const string CurrentDirectory = ".";
    private const string NoRepositoryPrefix = "mcp-gitops-no-repo-";
    private const string GitDirectoryName = ".git";
    private const string ModulesDirectory = "modules";
    private const string HopName = "hop";
    private const string JunctionSkipReason = "a directory junction is a Windows link; P6 covers a symbolic link elsewhere";
    private const string JunctionCreationSkipReason = "creating a directory junction is not permitted in this environment";
    private const string ShellScriptSkipReason = "a shell script cannot stand in for git on Windows";
    private const string OldGitFile = ".git/old-git/git";
    private const string OldGitToplevelFile = ".git/old-git/git.toplevel";
    private const string OldGitScript =
        "#!/bin/sh\nfor argument in \"$@\"; do last=\"$argument\"; done\nif [ \"$last\" = --show-toplevel ]; then cat \"$0.toplevel\"; else printf '%s\\n' \"$last\"; fi\n";
    private const string GitDirElsewhereMessage = "the git directory git reads for cwd belongs to another checkout";
    private const string HostilePrefix = "mcp-gitops-hostile-";
    private const string SeparateGitDirPrefix = "mcp-gitops-sepdir-";
    private const string WorktreesDirectory = "worktrees";
    private const string BacklinkFileName = "gitdir";
    private const string HeadFileName = "HEAD";
    private const string CommonDirFileName = "commondir";
    private const string ConfigFileName = "config";
    private const string MainHeadRef = "ref: refs/heads/main\n";
    private const string NonBareConfig = "[core]\n\trepositoryformatversion = 0\n\tbare = false\n";
    private const string RelativeBacklink = "../../../wt/.git\n";
    private const string ForgedDirectory = "forged";
    private const string FakeCommonDirectory = "fake";
    private const string ForgedWorktreeId = "x";
    private const string CheckoutName = "checkout";
    private const string StoreName = "store";
    private const string SeparateGitDirName = "proj.git";
    private const string RelativeCommonDirGitFile = ".git/common-dir-git/git";
    private const string FifoSkipReason = "a FIFO is a Unix file type; the other forged-worktree tests cover Windows";

    private readonly TempGitRepo _repo;

    public RepoRootResolverTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task ResolveAsync_CoreWorktreeNamingAnotherRepository_ThrowsNotAGitRepository()
    {
        // Arrange
        using var target = new TempGitRepo();
        _repo.ConfigureWorkTree(CurrentDirectory, target.RepoPath);
        var resolver = new RepoRootResolver(TestGit.Executable);

        // Act
        var act = () => resolver.ResolveAsync(_repo.RepoPath, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
        Assert.Equal(WorkTreeElsewhereMessage, exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_CoreWorktreeNamingTheRepositoryAroundIt_ThrowsNotAGitRepository()
    {
        // Arrange
        _repo.Run("init", "-b", "main", NestedRepository);
        _repo.ConfigureWorkTree(NestedRepository, _repo.RepoPath);
        var resolver = new RepoRootResolver(TestGit.Executable);

        // Act
        var act = () => resolver.ResolveAsync(Path.Combine(_repo.RepoPath, NestedRepository), TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
        Assert.Equal(WorkTreeElsewhereMessage, exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_CoreWorktreeNamingADirectoryInNoRepository_ThrowsNotAGitRepository()
    {
        // Arrange
        var outside = Directory.CreateTempSubdirectory(NoRepositoryPrefix);
        try
        {
            _repo.ConfigureWorkTree(CurrentDirectory, TempGitRepo.RealPath(outside.FullName));
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(_repo.RepoPath, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(WorkTreeElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(outside.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_RefusedCwd_IsRefusedAgain()
    {
        // Arrange
        using var target = new TempGitRepo();
        _repo.ConfigureWorkTree(CurrentDirectory, target.RepoPath);
        var resolver = new RepoRootResolver(TestGit.Executable);
        var act = () => resolver.ResolveAsync(_repo.RepoPath, TestContext.Current.CancellationToken);
        await Record.ExceptionAsync(act);

        // Act
        var exception = await Record.ExceptionAsync(act);

        // Assert
        Assert.IsType<NotAGitRepositoryException>(exception);
        Assert.Equal(WorkTreeElsewhereMessage, exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_CwdInsideThePlainGitDirectory_ThrowsNotAGitRepository()
    {
        // Arrange
        var resolver = new RepoRootResolver(TestGit.Executable);

        // Act
        var act = () => resolver.ResolveAsync(Path.Combine(_repo.RepoPath, GitDirectoryName), TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
    }

    [Fact]
    public async Task ResolveAsync_GitWithoutAbsoluteGitDir_ThrowsNotAGitRepository()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(ShellScriptSkipReason);
        }

        var resolver = new RepoRootResolver(WriteGitBefore2130());

        // Act
        var act = () => resolver.ResolveAsync(_repo.RepoPath, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
        Assert.Equal(WorkTreeElsewhereMessage, exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_SubdirectoryOfAPlainRepository_ReturnsTheRepositoryRoot()
    {
        // Arrange
        _repo.WriteFile($"{Subdirectory}/b.txt", "two\n");
        var resolver = new RepoRootResolver(TestGit.Executable);

        // Act
        var resolution = await resolver.ResolveAsync(Path.Combine(_repo.RepoPath, Subdirectory), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Path.GetFullPath(_repo.RepoPath), Path.GetFullPath(resolution.Root));
    }

    [Fact]
    public async Task ResolveAsync_SubmoduleCheckout_ReturnsTheSubmoduleRoot()
    {
        // Arrange
        using var source = NewSubmoduleSource();
        _repo.AddSubmoduleFrom(source.RepoPath, SubmodulePath);
        var checkout = Path.Combine(_repo.RepoPath, SubmodulePath);
        var resolver = new RepoRootResolver(TestGit.Executable);

        // Act
        var resolution = await resolver.ResolveAsync(checkout, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Path.GetFullPath(checkout), Path.GetFullPath(resolution.Root));
    }

    [Fact]
    public async Task ResolveAsync_SubmoduleGitDirectory_ReturnsTheSubmoduleRoot()
    {
        // Arrange
        using var source = NewSubmoduleSource();
        _repo.AddSubmoduleFrom(source.RepoPath, SubmodulePath);
        var resolver = new RepoRootResolver(TestGit.Executable);

        // Act
        var resolution = await resolver.ResolveAsync(SubmoduleGitDirectory(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Path.GetFullPath(Path.Combine(_repo.RepoPath, SubmodulePath)), Path.GetFullPath(resolution.Root));
    }

    [Fact]
    public async Task ResolveAsync_LinkedWorktree_ReturnsTheWorktreeRoot()
    {
        // Arrange
        _repo.AddLinkedWorktree(LinkedWorktreePath, LinkedWorktreeBranch);
        var worktree = Path.Combine(_repo.RepoPath, LinkedWorktreePath);
        var resolver = new RepoRootResolver(TestGit.Executable);

        // Act
        var resolution = await resolver.ResolveAsync(worktree, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Path.GetFullPath(worktree), Path.GetFullPath(resolution.Root));
    }

    [Fact]
    public async Task ResolveAsync_CwdThroughASymbolicLink_ReturnsTheRealRoot()
    {
        // Arrange
        var linkParent = Directory.CreateTempSubdirectory(LinkParentPrefix);
        try
        {
            var link = Path.Combine(linkParent.FullName, LinkName);
            if (!TryCreateDirectoryLink(link, _repo.RepoPath))
            {
                Assert.Skip(SymbolicLinkSkipReason);
            }

            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var resolution = await resolver.ResolveAsync(link, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(Path.GetFullPath(_repo.RepoPath), Path.GetFullPath(resolution.Root));
        }
        finally
        {
            Directory.Delete(linkParent.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_SubmoduleGitDirectoryThroughASymbolicLink_ReturnsTheSubmoduleRoot()
    {
        // Arrange
        using var source = NewSubmoduleSource();
        _repo.AddSubmoduleFrom(source.RepoPath, SubmodulePath);
        var linkParent = Directory.CreateTempSubdirectory(LinkParentPrefix);
        try
        {
            var link = Path.Combine(linkParent.FullName, LinkName);
            if (!TryCreateDirectoryLink(link, SubmoduleGitDirectory()))
            {
                Assert.Skip(SymbolicLinkSkipReason);
            }

            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var resolution = await resolver.ResolveAsync(link, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(Path.GetFullPath(Path.Combine(_repo.RepoPath, SubmodulePath)), Path.GetFullPath(resolution.Root));
        }
        finally
        {
            Directory.Delete(linkParent.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_SubmoduleGitDirectoryThroughALinkWhoseTargetCrossesALink_ReturnsTheSubmoduleRoot()
    {
        // Arrange
        using var source = NewSubmoduleSource();
        _repo.AddSubmoduleFrom(source.RepoPath, SubmodulePath);
        var linkParent = Directory.CreateTempSubdirectory(LinkParentPrefix);
        try
        {
            var hop = Path.Combine(linkParent.FullName, HopName);
            var link = Path.Combine(linkParent.FullName, LinkName);
            var throughHop = Path.Combine(hop, GitDirectoryName, ModulesDirectory, SubmodulePath);
            if (!TryCreateDirectoryLink(hop, _repo.RepoPath) || !TryCreateDirectoryLink(link, throughHop))
            {
                Assert.Skip(SymbolicLinkSkipReason);
            }

            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var resolution = await resolver.ResolveAsync(link, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(Path.GetFullPath(Path.Combine(_repo.RepoPath, SubmodulePath)), Path.GetFullPath(resolution.Root));
        }
        finally
        {
            Directory.Delete(linkParent.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_SubmoduleGitDirectoryThroughAJunction_ReturnsTheSubmoduleRoot()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(JunctionSkipReason);
        }

        using var source = NewSubmoduleSource();
        _repo.AddSubmoduleFrom(source.RepoPath, SubmodulePath);
        var linkParent = Directory.CreateTempSubdirectory(LinkParentPrefix);
        var link = Path.Combine(linkParent.FullName, LinkName);
        try
        {
            if (!TryCreateJunction(link, SubmoduleGitDirectory()))
            {
                Assert.Skip(JunctionCreationSkipReason);
            }

            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var resolution = await resolver.ResolveAsync(link, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(Path.GetFullPath(Path.Combine(_repo.RepoPath, SubmodulePath)), Path.GetFullPath(resolution.Root));
        }
        finally
        {
            DeleteJunctionThenParent(link, linkParent);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitFileNamingAnotherCheckoutsGitDirectory_ThrowsNotAGitRepository()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var hostile = TempGitRepo.RealPath(folder.FullName);
            WriteGitFile(hostile, $"../{Path.GetFileName(_repo.RepoPath)}/{GitDirectoryName}");
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(hostile, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitDirectoryWithACommondirNamingAnotherRepository_ThrowsNotAGitRepository()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var hostile = TempGitRepo.RealPath(folder.FullName);
            WriteForgedGitDirectory(Path.Combine(hostile, GitDirectoryName), Path.Combine(_repo.RepoPath, GitDirectoryName), backlink: null);
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(hostile, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitFileNamingAnotherCheckoutsLinkedWorktree_ThrowsNotAGitRepository()
    {
        // Arrange
        _repo.AddLinkedWorktree(LinkedWorktreePath, LinkedWorktreeBranch);
        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var hostile = TempGitRepo.RealPath(folder.FullName);
            WriteGitFile(hostile, LinkedWorktreeGitDirectory());
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(hostile, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitFileNamingAWorktreeDirectoryWithoutABacklink_ThrowsNotAGitRepository()
    {
        // Arrange
        _repo.AddLinkedWorktree(LinkedWorktreePath, LinkedWorktreeBranch);
        File.Delete(Path.Combine(LinkedWorktreeGitDirectory(), BacklinkFileName));
        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var hostile = TempGitRepo.RealPath(folder.FullName);
            WriteGitFile(hostile, LinkedWorktreeGitDirectory());
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(hostile, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitFileNamingAForgedWorktreeDirectory_ThrowsNotAGitRepository()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var hostile = TempGitRepo.RealPath(folder.FullName);
            var forged = Path.Combine(hostile, ForgedDirectory, ForgedWorktreeId);
            WriteForgedGitDirectory(forged, Path.Combine(_repo.RepoPath, GitDirectoryName), Path.Combine(hostile, GitDirectoryName));
            WriteGitFile(hostile, forged);
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(hostile, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task ResolveAsync_ForgedWorktreeWhoseBacklinkIsAFifo_ThrowsNotAGitRepository()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(FifoSkipReason);
        }

        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var hostile = TempGitRepo.RealPath(folder.FullName);
            var fakeCommon = Path.Combine(hostile, FakeCommonDirectory);
            WriteFakeCommonDirectory(fakeCommon);
            var worktree = Path.Combine(fakeCommon, WorktreesDirectory, ForgedWorktreeId);
            WriteForgedGitDirectory(worktree, "../..", backlink: null);
            MakeFifo(Path.Combine(worktree, BacklinkFileName));
            WriteGitFile(hostile, worktree);
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(hostile, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitLinkToAnotherCheckoutsGitDirectory_ThrowsNotAGitRepository()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var hostile = TempGitRepo.RealPath(folder.FullName);
            if (!TryCreateDirectoryLink(Path.Combine(hostile, GitDirectoryName), Path.Combine(_repo.RepoPath, GitDirectoryName)))
            {
                Assert.Skip(SymbolicLinkSkipReason);
            }

            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(hostile, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            DeleteLinkThenParent(Path.Combine(folder.FullName, GitDirectoryName), folder);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitJunctionToAnotherCheckoutsGitDirectory_ThrowsNotAGitRepository()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(JunctionSkipReason);
        }

        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        var link = Path.Combine(folder.FullName, GitDirectoryName);
        try
        {
            if (!TryCreateJunction(link, Path.Combine(_repo.RepoPath, GitDirectoryName)))
            {
                Assert.Skip(JunctionCreationSkipReason);
            }

            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(TempGitRepo.RealPath(folder.FullName), TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            DeleteJunctionThenParent(link, folder);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitFileNamingADeinitializedSubmoduleGitDirectory_ThrowsNotAGitRepository()
    {
        // Arrange
        using var source = NewSubmoduleSource();
        _repo.AddSubmoduleFrom(source.RepoPath, SubmodulePath);
        _repo.Run("submodule", "deinit", "-f", SubmodulePath);
        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var hostile = TempGitRepo.RealPath(folder.FullName);
            WriteGitFile(hostile, SubmoduleGitDirectory());
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(hostile, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitFileNamingAGitDirectoryBesideAnotherCheckoutsGitDirectory_ThrowsNotAGitRepository()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory(HostilePrefix);
        try
        {
            var checkout = Path.Combine(TempGitRepo.RealPath(folder.FullName), CheckoutName);
            _repo.Run("init", "-b", "main", "--separate-git-dir", Path.Combine(_repo.RepoPath, SeparateGitDirName), checkout);
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var act = () => resolver.ResolveAsync(checkout, TestContext.Current.CancellationToken);

            // Assert
            var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
            Assert.Equal(GitDirElsewhereMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitPrintingARelativeCommonDir_ThrowsNotAGitRepository()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(ShellScriptSkipReason);
        }

        var resolver = new RepoRootResolver(WriteGitWithRelativeCommonDir());

        // Act
        var act = () => resolver.ResolveAsync(_repo.RepoPath, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<NotAGitRepositoryException>(act);
        Assert.Equal(GitDirElsewhereMessage, exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_LinkedWorktreeWithARelativeBacklink_ReturnsTheWorktreeRoot()
    {
        // Arrange
        _repo.AddLinkedWorktree(LinkedWorktreePath, LinkedWorktreeBranch);
        File.WriteAllText(Path.Combine(LinkedWorktreeGitDirectory(), BacklinkFileName), RelativeBacklink);
        var worktree = Path.Combine(_repo.RepoPath, LinkedWorktreePath);
        var resolver = new RepoRootResolver(TestGit.Executable);

        // Act
        var resolution = await resolver.ResolveAsync(worktree, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Path.GetFullPath(worktree), Path.GetFullPath(resolution.Root));
    }

    [Fact]
    public async Task ResolveAsync_SeparateGitDirCheckout_ReturnsTheCheckoutRoot()
    {
        // Arrange
        var parent = Directory.CreateTempSubdirectory(SeparateGitDirPrefix);
        try
        {
            var checkout = AddSeparateGitDirCheckout(TempGitRepo.RealPath(parent.FullName));
            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var resolution = await resolver.ResolveAsync(checkout, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(Path.GetFullPath(checkout), Path.GetFullPath(resolution.Root));
        }
        finally
        {
            Directory.Delete(parent.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_GitLinkToAGitDirectoryNotNamedGit_ReturnsTheRoot()
    {
        // Arrange
        using var repo = new TempGitRepo();
        var parent = Directory.CreateTempSubdirectory(SeparateGitDirPrefix);
        var dotGit = Path.Combine(repo.RepoPath, GitDirectoryName);
        try
        {
            var moved = Path.Combine(TempGitRepo.RealPath(parent.FullName), StoreName, SeparateGitDirName);
            Directory.CreateDirectory(Path.GetDirectoryName(moved));
            Directory.Move(dotGit, moved);
            if (!TryCreateDirectoryLink(dotGit, moved))
            {
                Assert.Skip(SymbolicLinkSkipReason);
            }

            var resolver = new RepoRootResolver(TestGit.Executable);

            // Act
            var resolution = await resolver.ResolveAsync(repo.RepoPath, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(Path.GetFullPath(repo.RepoPath), Path.GetFullPath(resolution.Root));
        }
        finally
        {
            DeleteLinkThenParent(dotGit, parent);
        }
    }

    #region Test Helpers

    /// <summary>A repository with one committed file, for <see cref="TempGitRepo.AddSubmoduleFrom"/> to clone.</summary>
    /// <returns>The repository; the caller disposes it.</returns>
    private static TempGitRepo NewSubmoduleSource()
    {
        var source = new TempGitRepo();
        source.WriteFile(SubmoduleFile, "sub\n");
        source.Run("add", ".");
        source.Run("commit", "-m", "submodule");
        return source;
    }

    /// <summary>The git directory git keeps for the submodule at <see cref="SubmodulePath"/>, under <c>.git/modules/</c>.</summary>
    /// <returns>The directory's absolute path.</returns>
    private string SubmoduleGitDirectory()
        => Path.Combine(_repo.RepoPath, GitDirectoryName, ModulesDirectory, SubmodulePath);

    /// <summary>The git directory git keeps for the linked worktree at <see cref="LinkedWorktreePath"/>, under <c>.git/worktrees/</c>.</summary>
    /// <returns>The directory's absolute path.</returns>
    private string LinkedWorktreeGitDirectory()
        => Path.Combine(_repo.RepoPath, GitDirectoryName, WorktreesDirectory, LinkedWorktreePath);

    /// <summary>Write a <c>.git</c> file in <paramref name="folder"/> naming <paramref name="gitDir"/>, the way a linked worktree's checkout does.</summary>
    /// <param name="folder">The folder that gets the file.</param>
    /// <param name="gitDir">The <c>gitdir:</c> value, absolute or relative to <paramref name="folder"/>.</param>
    private static void WriteGitFile(string folder, string gitDir)
        => File.WriteAllText(Path.Combine(folder, GitDirectoryName), $"gitdir: {gitDir}\n");

    /// <summary>
    /// Create <paramref name="gitDir"/> as a git directory that holds only <c>HEAD</c> and a <c>commondir</c> naming
    /// <paramref name="commonDir"/>, plus a <c>gitdir</c> backlink when <paramref name="backlink"/> is set: the files a
    /// linked worktree's directory under <c>.git/worktrees/</c> holds.
    /// </summary>
    /// <param name="gitDir">The directory to create.</param>
    /// <param name="commonDir">The <c>commondir</c> value, absolute or relative to <paramref name="gitDir"/>.</param>
    /// <param name="backlink">The <c>gitdir</c> file's value, or <c>null</c> to write none.</param>
    private static void WriteForgedGitDirectory(string gitDir, string commonDir, string backlink)
    {
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, HeadFileName), MainHeadRef);
        File.WriteAllText(Path.Combine(gitDir, CommonDirFileName), commonDir + "\n");
        if (backlink is not null)
        {
            File.WriteAllText(Path.Combine(gitDir, BacklinkFileName), backlink + "\n");
        }
    }

    /// <summary>Create <paramref name="commonDir"/> as an empty, non-bare common directory: <c>HEAD</c>, <c>config</c>, <c>objects/</c>, <c>refs/</c>.</summary>
    /// <param name="commonDir">The directory to create.</param>
    private static void WriteFakeCommonDirectory(string commonDir)
    {
        Directory.CreateDirectory(Path.Combine(commonDir, "objects"));
        Directory.CreateDirectory(Path.Combine(commonDir, "refs"));
        File.WriteAllText(Path.Combine(commonDir, HeadFileName), MainHeadRef);
        File.WriteAllText(Path.Combine(commonDir, ConfigFileName), NonBareConfig);
    }

    /// <summary>
    /// Run <c>git init --separate-git-dir</c> inside <paramref name="parent"/>: the checkout at <c>checkout/</c> gets a
    /// <c>.git</c> file naming <c>store/proj.git</c>, and git writes no <c>core.worktree</c> and no backlink.
    /// </summary>
    /// <param name="parent">An empty temporary folder, as a real path.</param>
    /// <returns>The checkout's absolute path.</returns>
    private string AddSeparateGitDirCheckout(string parent)
    {
        var checkout = Path.Combine(parent, CheckoutName);
        var store = Directory.CreateDirectory(Path.Combine(parent, StoreName));
        _repo.Run("init", "-b", "main", "--separate-git-dir", Path.Combine(store.FullName, SeparateGitDirName), checkout);
        return checkout;
    }

    /// <summary>Create a FIFO at <paramref name="path"/> with <c>mkfifo</c> (Linux and macOS).</summary>
    /// <param name="path">Where to create it.</param>
    /// <exception cref="InvalidOperationException">Thrown when <c>mkfifo</c> fails.</exception>
    private static void MakeFifo(string path)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/env") { UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("mkfifo");
        startInfo.ArgumentList.Add(path);
        using var process = Process.Start(startInfo);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"mkfifo exited {process.ExitCode}");
        }
    }

    /// <summary>Delete <paramref name="link"/> on its own when it is a link, leaving its target alone, then <paramref name="parent"/>.</summary>
    /// <param name="link">A path that may hold a directory symbolic link.</param>
    /// <param name="parent">The temporary folder to delete afterwards.</param>
    private static void DeleteLinkThenParent(string link, DirectoryInfo parent)
    {
        if (Directory.Exists(link) && new DirectoryInfo(link).LinkTarget is not null)
        {
            Directory.Delete(link);
        }

        Directory.Delete(parent.FullName, recursive: true);
    }

    /// <summary>
    /// Write a stand-in that answers the way git before 2.13.0 does: <c>rev-parse --show-toplevel</c> prints the
    /// repository root, and any other last argument, such as the <c>--absolute-git-dir</c> that release does not know, is
    /// printed back with exit code 0. The root sits in a file next to the script because git's environment carries only
    /// the variables the server allowlists, and <c>cat</c> resolves through the <c>PATH</c> it keeps.
    /// </summary>
    /// <returns>The stand-in's absolute path.</returns>
    private string WriteGitBefore2130()
    {
        _repo.WriteFile(OldGitToplevelFile, _repo.RepoPath + "\n");
        _repo.WriteFile(OldGitFile, OldGitScript);
        var script = Path.Combine(_repo.RepoPath, OldGitFile);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return script;
    }

    /// <summary>
    /// Write a stand-in that runs the real git for every call except <c>rev-parse ... --git-common-dir</c>, which it
    /// answers with the relative path <c>.git</c>, the shape a git that ignores <c>--path-format=absolute</c> prints.
    /// </summary>
    /// <returns>The stand-in's absolute path.</returns>
    private string WriteGitWithRelativeCommonDir()
    {
        _repo.WriteFile(RelativeCommonDirGitFile, $"#!/bin/sh\nfor argument in \"$@\"; do last=\"$argument\"; done\nif [ \"$last\" = --git-common-dir ]; then printf '.git\\n'; exit 0; fi\nexec '{TestGit.Executable}' \"$@\"\n");
        var script = Path.Combine(_repo.RepoPath, RelativeCommonDirGitFile);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return script;
    }

    /// <summary>Create a directory symbolic link at <paramref name="link"/> naming <paramref name="target"/>.</summary>
    /// <param name="link">The link's path.</param>
    /// <param name="target">The directory it names.</param>
    /// <returns><c>false</c> when this environment does not permit creating one.</returns>
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Create a directory junction at <paramref name="link"/> naming <paramref name="target"/> with <c>mklink /J</c>.</summary>
    /// <param name="link">The junction's path.</param>
    /// <param name="target">The directory it names.</param>
    /// <returns>Whether the junction exists afterwards.</returns>
    private static bool TryCreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(link);
    }

    /// <summary>
    /// Delete the junction at <paramref name="link"/> on its own, which removes the reparse point and leaves its target
    /// alone, then <paramref name="linkParent"/>; a recursive delete of a folder that still holds a junction tries to
    /// unmount it and fails without elevation.
    /// </summary>
    /// <param name="link">The junction's path.</param>
    /// <param name="linkParent">The temporary folder holding it.</param>
    private static void DeleteJunctionThenParent(string link, DirectoryInfo linkParent)
    {
        if (Directory.Exists(link))
        {
            Directory.Delete(link);
        }

        Directory.Delete(linkParent.FullName, recursive: true);
    }

    #endregion
}