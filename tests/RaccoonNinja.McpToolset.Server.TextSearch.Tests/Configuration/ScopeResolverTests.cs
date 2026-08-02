using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Configuration;

public sealed class ScopeResolverTests : IDisposable
{
    private static readonly SearchConfig Config = new() { MaxFileBytes = SearchConfig.DefaultMaxFileBytes };

    private readonly List<string> _cleanup = [];

    [Fact]
    public void Create_NoBaseRoot_FailsFast()
    {
        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, "", null, null));
    }

    [Fact]
    public void Create_NonexistentBase_FailsFast()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), $"rnmcp-missing-{Guid.NewGuid():N}");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, missing, null, null));
    }

    [Fact]
    public void Create_BaseLeafIsReparentUnsafe_FailsFast()
    {
        // Arrange
        var parent = NewTempDirectory("reparent-unsafe-leaf");
        var config = Directory.CreateDirectory(Path.Combine(parent, ".config")).FullName;

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, config, null, null));
    }

    [Fact]
    public void Create_BaseUnderDenylistedSegment_FailsFast()
    {
        // Arrange
        var parent = NewTempDirectory("denylisted-segment");
        var under = Directory.CreateDirectory(Path.Combine(parent, ".ssh", "proj")).FullName;

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, under, null, null));
    }

    [Fact]
    public void Create_BadExtraDeny_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("extra-deny");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, null, "/etc/passwd"));
    }

    [Fact]
    public void Resolve_BlankCwd_ReturnsWholeBaseScope()
    {
        // Arrange
        var resolver = ScopeResolver.Create(Config, NewTempDirectory("whole"), null, null);

        // Act
        var scope = resolver.Resolve(null);

        // Assert
        Assert.Equal(".", scope.ScopeKey);
    }

    [Fact]
    public void Resolve_ValidSubdir_ScopeKeyIsRelative()
    {
        // Arrange
        var baseRoot = NewTempDirectory("subdir");
        var project = Directory.CreateDirectory(Path.Combine(baseRoot, "proj")).FullName;
        var resolver = ScopeResolver.Create(Config, baseRoot, null, null);

        // Act
        var scope = resolver.Resolve(project);

        // Assert
        Assert.Equal("proj", scope.ScopeKey);
    }

    [Fact]
    public void Resolve_CwdOutsideBase_IsRefused()
    {
        // Arrange
        var resolver = ScopeResolver.Create(Config, NewTempDirectory("in"), null, null);
        var outside = NewTempDirectory("out");

        // Act
        var exception = Assert.Throws<TextSearchException>(() => resolver.Resolve(outside));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal("cwd_outside_base", exception.RefusalReason);
    }

    [Fact]
    public void Resolve_CwdIsFile_IsRefused()
    {
        // Arrange
        var baseRoot = NewTempDirectory("file-cwd");
        var file = Path.Combine(baseRoot, "note.txt");
        File.WriteAllText(file, "x");
        var resolver = ScopeResolver.Create(Config, baseRoot, null, null);

        // Act
        var exception = Assert.Throws<TextSearchException>(() => resolver.Resolve(file));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal("cwd_not_a_directory", exception.RefusalReason);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".aws")]
    public void Resolve_CwdInsideDenylistedDir_IsRefused(string denied)
    {
        // Arrange
        var baseRoot = NewTempDirectory("denied-cwd");
        var deniedDir = Directory.CreateDirectory(Path.Combine(baseRoot, denied)).FullName;
        var resolver = ScopeResolver.Create(Config, baseRoot, null, null);

        // Act
        var exception = Assert.Throws<TextSearchException>(() => resolver.Resolve(deniedDir));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal("cwd_denylisted", exception.RefusalReason);
    }

    [Fact]
    public void Resolve_CwdLeafIsReparentUnsafe_IsRefused()
    {
        // Arrange
        var baseRoot = NewTempDirectory("reparent-cwd");
        var configDir = Directory.CreateDirectory(Path.Combine(baseRoot, ".config")).FullName;
        var resolver = ScopeResolver.Create(Config, baseRoot, null, null);

        // Act
        var exception = Assert.Throws<TextSearchException>(() => resolver.Resolve(configDir));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal("cwd_denylisted", exception.RefusalReason);
    }

    [Fact]
    public void Resolve_CwdInGcloudPair_IsRefused()
    {
        // Arrange
        var baseRoot = NewTempDirectory("gcloud-pair-cwd");
        var gcloud = Directory.CreateDirectory(Path.Combine(baseRoot, ".config", "gcloud")).FullName;
        var resolver = ScopeResolver.Create(Config, baseRoot, null, null);

        // Act
        var exception = Assert.Throws<TextSearchException>(() => resolver.Resolve(gcloud));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal("cwd_denylisted", exception.RefusalReason);
    }

    [Fact]
    public void Create_BaseInsideGcloudPair_FailsFast()
    {
        // Arrange
        var parent = NewTempDirectory("gcloud-base");
        var gcloud = Directory.CreateDirectory(Path.Combine(parent, ".config", "gcloud")).FullName;

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, gcloud, null, null));
    }

    [Fact]
    public void Create_BaseIsUserHome_FailsFast()
    {
        // Arrange
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, home, null, null));
    }

    [Fact]
    public void Create_BaseIsFilesystemRoot_FailsFast()
    {
        // Arrange
        var root = Path.GetPathRoot(Path.GetTempPath());

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, root, null, null));
    }

    [Fact]
    public void Create_DefaultIgnoreFile_AppliesItsPatterns()
    {
        // Arrange
        var baseRoot = NewTempDirectory("default-ignore-file");
        var ignoreFile = Path.Combine(baseRoot, "custom.ignore");
        File.WriteAllText(ignoreFile, "custom_dir/\n");

        // Act
        var resolver = ScopeResolver.Create(Config, baseRoot, ignoreFile, null);

        // Assert
        Assert.Contains("custom_dir/", resolver.DefaultIgnorePatterns);
    }

    [Fact]
    public void Create_DefaultIgnoreMissingFile_FailsFast()
    {
        // Arrange
        var baseRoot = NewTempDirectory("default-ignore-missing");
        var missing = Path.Combine(baseRoot, "nope.ignore");

        // Act & Assert
        Assert.Throws<SearchStartupException>(() => ScopeResolver.Create(Config, baseRoot, missing, null));
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
        var dir = Path.Combine(Path.GetTempPath(), $"rnmcp-scope-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _cleanup.Add(dir);
        return dir;
    }
}