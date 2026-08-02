using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Configuration;

/// <summary>
/// Startup validation and resolution for the opt-in package roots: name rules, per-root broad-root guard,
/// the canonical bidirectional overlap check (including a symlink that resolves a "disjoint" cache into the
/// base), and whole-cache resolution. These construct the resolver directly with manual temp directories
/// because several cases (overlap, junctions, filesystem roots) cannot be expressed through the tool harness.
/// </summary>
public sealed class PackageRootResolverTests : IDisposable
{
    private static readonly SearchConfig Config = new() { MaxFileBytes = SearchConfig.DefaultMaxFileBytes };

    private readonly List<string> _cleanup = [];

    [Fact]
    public void Create_ValidPackageRoot_ResolvesWholeCache()
    {
        // Arrange
        var baseRoot = NewTempDirectory("pkg-ok-base");
        var pkg = NewTempDirectory("pkg-ok");
        var resolver = ScopeResolver.Create(Config, baseRoot, null, null, $"nuget={pkg}");

        // Act
        var scope = resolver.Resolve("@nuget");

        // Assert
        Assert.Equal(ScopeKind.Package, scope.Kind);
        Assert.Equal("@nuget", scope.ScopeKey);
        Assert.Equal(["nuget"], resolver.PackageRootNames);
    }

    [Fact]
    public void Create_BarePackagePath_DerivesNameFromBasename()
    {
        // Arrange
        var baseRoot = NewTempDirectory("bare-base");
        var parent = NewTempDirectory("bare-parent");
        var pkg = Directory.CreateDirectory(Path.Combine(parent, "mycache")).FullName;

        // Act
        var resolver = ScopeResolver.Create(Config, baseRoot, null, null, pkg);

        // Assert
        Assert.Equal(["mycache"], resolver.PackageRootNames);
        Assert.Equal("@mycache", resolver.Resolve("@mycache").ScopeKey);
    }

    [Fact]
    public void Resolve_UnknownPackageName_IsRefused()
    {
        // Arrange
        var resolver = ScopeResolver.Create(Config, NewTempDirectory("unknown-base"), null, null, $"nuget={NewTempDirectory("unknown-pkg")}");

        // Act
        var exception = Assert.Throws<TextSearchException>(() => resolver.Resolve("@cargo"));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal("cwd_unknown_package_root", exception.RefusalReason);
    }

    [Fact]
    public void Resolve_PackageSubpathEscaping_IsRefused()
    {
        // Arrange
        var resolver = ScopeResolver.Create(Config, NewTempDirectory("escape-base"), null, null, $"nuget={NewTempDirectory("escape-pkg")}");

        // Act
        var exception = Assert.Throws<TextSearchException>(() => resolver.Resolve("@nuget/../.."));

        // Assert
        Assert.Equal("cwd_outside_package_root", exception.RefusalReason);
    }

    [Fact]
    public void Create_DuplicatePackageNamesCaseInsensitive_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("dup-base");
        var a = NewTempDirectory("dup-a");
        var b = NewTempDirectory("dup-b");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"nuget={a};NuGet={b}"));
    }

    [Fact]
    public void Create_PackageNameStartingWithAt_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("at-base");
        var pkg = NewTempDirectory("at-pkg");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"@bad={pkg}"));
    }

    [Fact]
    public void Create_PackageNameWithPathSeparator_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("sep-base");
        var pkg = NewTempDirectory("sep-pkg");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"a/b={pkg}"));
    }

    [Fact]
    public void Create_PackageNameWithControlCharacter_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("ctrl-base");
        var pkg = NewTempDirectory("ctrl-pkg");
        var name = "a" + (char)0x1E + "b"; // a U+001E record separator inside the name

        // Act & Assert: a U+001E in the name would collide with the cursor-identity separator.
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"{name}={pkg}"));
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@/sub")]
    public void Resolve_MalformedPackageReference_IsUnknown(string cwd)
    {
        // Arrange
        var resolver = ScopeResolver.Create(Config, NewTempDirectory("malformed-base"), null, null, $"nuget={NewTempDirectory("malformed-pkg")}");

        // Act
        var exception = Assert.Throws<TextSearchException>(() => resolver.Resolve(cwd));

        // Assert
        Assert.Equal("cwd_unknown_package_root", exception.RefusalReason);
    }

    [Fact]
    public void Create_PackageNameIsDotDot_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("dotdot-base");
        var pkg = NewTempDirectory("dotdot-pkg");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"..={pkg}"));
    }

    [Fact]
    public void Create_PackageEntryWithNoPath_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("nopath-base");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, "nuget="));
    }

    [Fact]
    public void Create_NonexistentPackagePath_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("missing-pkg-base");
        var missing = Path.Combine(Path.GetTempPath(), $"rnmcp-missing-pkg-{Guid.NewGuid():N}");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"nuget={missing}"));
    }

    [Fact]
    public void Create_PackageRootUnderBase_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("overlap-under-base");
        var under = Directory.CreateDirectory(Path.Combine(baseRoot, "cache")).FullName;

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"cache={under}"));
    }

    [Fact]
    public void Create_BaseUnderPackageRoot_FailsFast()
    {
        // Arrange
        var pkg = NewTempDirectory("overlap-pkg-parent");
        var baseRoot = Directory.CreateDirectory(Path.Combine(pkg, "proj")).FullName;

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"pkg={pkg}"));
    }

    [Fact]
    public void Create_TwoPackageRootsSamePath_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("same-path-base");
        var pkg = NewTempDirectory("same-path-pkg");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"a={pkg};b={pkg}"));
    }

    [Fact]
    public void Create_PackageRootIsFilesystemRoot_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("fsroot-base");
        var root = Path.GetPathRoot(Path.GetTempPath());

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"root={root}"));
    }

    [Fact]
    public void Create_PackageRootUnderDenylistedSegment_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("pkg-denyseg-base");
        var parent = NewTempDirectory("pkg-denyseg");
        var under = Directory.CreateDirectory(Path.Combine(parent, ".ssh", "cache")).FullName;

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"c={under}"));
    }

    [Fact]
    public void Create_PackageRootInsideGcloudPair_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("pkg-gcloud-base");
        var parent = NewTempDirectory("pkg-gcloud");
        var gcloud = Directory.CreateDirectory(Path.Combine(parent, ".config", "gcloud")).FullName;

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"g={gcloud}"));
    }

    [Fact]
    public void Create_PackageRootLinkedIntoBase_FailsFast()
    {
        // Arrange
        var parent = NewTempDirectory("junction");
        var baseRoot = Directory.CreateDirectory(Path.Combine(parent, "base")).FullName;
        var realTarget = Directory.CreateDirectory(Path.Combine(baseRoot, "inside")).FullName;
        var link = Path.Combine(parent, "link");
        if (!TryCreateDirectoryLink(link, realTarget))
        {
            Assert.Skip("creating a directory link (junction/symlink) is not permitted in this environment");
        }

        // Act & Assert: a lexical check sees link as disjoint from base, but it resolves inside base, so the
        // canonical, symlink-resolving overlap check must reject it.
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, null, $"pkg={link}"));
    }

    /// <summary>
    /// Create a directory link at <paramref name="link"/> pointing at <paramref name="target"/>: a junction
    /// on Windows (unprivileged), a symbolic link elsewhere. Returns false if the environment forbids it.
    /// </summary>
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });
                process.WaitForExit();
                return process.ExitCode == 0 && Directory.Exists(link);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public void Create_NoPackageRoots_LeavesNamesEmpty()
    {
        // Arrange & Act
        var resolver = ScopeResolver.Create(Config, NewTempDirectory("no-pkg-base"), null, null);

        // Assert
        Assert.Empty(resolver.PackageRootNames);
    }

    public void Dispose()
    {
        foreach (var dir in _cleanup)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private string NewTempDirectory(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rnmcp-pkg-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _cleanup.Add(dir);
        return dir;
    }
}