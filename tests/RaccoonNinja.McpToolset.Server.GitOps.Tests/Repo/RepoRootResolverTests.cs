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