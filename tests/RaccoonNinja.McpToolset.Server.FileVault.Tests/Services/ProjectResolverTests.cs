using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Services;

/// <summary>Tests for <see cref="ProjectResolver.Resolve"/>: priority order, trimming, validation, and cwd derivation.</summary>
public sealed class ProjectResolverTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));

    public ProjectResolverTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp cleaner reclaims leftovers.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort; the OS temp cleaner reclaims leftovers.
        }
    }

    [Fact]
    public void Resolve_ExplicitArgument_WinsOverEnvOverrideAndCwd()
    {
        // Arrange
        var cwd = CreateDir("cwd-project");
        var resolver = new ProjectResolver(ConfigWith("env-project"), cwd);

        // Act
        var project = resolver.Resolve("explicit-project");

        // Assert
        Assert.Equal("explicit-project", project.Value);
    }

    [Fact]
    public void Resolve_ExplicitArgumentWithPadding_IsTrimmed()
    {
        // Arrange
        var cwd = CreateDir("cwd-project");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);

        // Act
        var project = resolver.Resolve("  padded-project  ");

        // Assert
        Assert.Equal("padded-project", project.Value);
    }

    [Fact]
    public void Resolve_WhitespaceOnlyExplicit_FallsThroughToEnvOverride()
    {
        // Arrange
        var cwd = CreateDir("cwd-project");
        var resolver = new ProjectResolver(ConfigWith("env-project"), cwd);

        // Act
        var project = resolver.Resolve("   ");

        // Assert
        Assert.Equal("env-project", project.Value);
    }

    [Fact]
    public void Resolve_NoExplicitWithEnvOverride_UsesEnvOverrideNotCwd()
    {
        // Arrange
        var cwd = CreateDir("cwd-project");
        var resolver = new ProjectResolver(ConfigWith("env-project"), cwd);

        // Act
        var project = resolver.Resolve(null);

        // Assert
        Assert.Equal("env-project", project.Value);
    }

    [Fact]
    public void Resolve_WhitespaceOnlyEnvOverride_FallsThroughToDerivation()
    {
        // Arrange
        var cwd = CreateDir("env-fallback-dir");
        File.WriteAllText(Path.Combine(cwd, "nx.json"), "{}");
        var resolver = new ProjectResolver(ConfigWith("   "), cwd);

        // Act
        var project = resolver.Resolve(null);

        // Assert
        Assert.Equal("env-fallback-dir", project.Value);
    }

    [Fact]
    public void Resolve_MalformedExplicitArgument_ThrowsInvalidName()
    {
        // Arrange
        var cwd = CreateDir("cwd-project");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);
        Action act = () => resolver.Resolve("bad name");

        // Act / Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }

    [Fact]
    public void Resolve_MalformedEnvOverride_ThrowsInvalidName()
    {
        // Arrange
        var cwd = CreateDir("cwd-project");
        var resolver = new ProjectResolver(ConfigWith("bad*name"), cwd);
        Action act = () => resolver.Resolve(null);

        // Act / Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }

    [Fact]
    public void Resolve_NoExplicitNoEnv_FallsThroughToCwdDerivedBasename()
    {
        // Arrange
        var cwd = CreateDir("solo-project-dir");
        File.WriteAllText(Path.Combine(cwd, "nx.json"), "{}");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);

        // Act
        var project = resolver.Resolve(null);

        // Assert
        Assert.Equal("solo-project-dir", project.Value);
    }

    [Fact]
    public void Resolve_CwdUnderWorkspaceMarkerRoot_DerivesRootSlashDirectory()
    {
        // Arrange
        var root = CreateDir("monorepo-root");
        File.WriteAllText(Path.Combine(root, "nx.json"), "{}");
        var cwd = CreateDir("monorepo-root", "webapp");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);

        // Act
        var project = resolver.Resolve(null);

        // Assert
        Assert.Equal("monorepo-root/webapp", project.Value);
    }

    [Fact]
    public void Resolve_CwdIsWorkspaceRoot_DerivesBareBasename()
    {
        // Arrange
        var cwd = CreateDir("workspace-root");
        File.WriteAllText(Path.Combine(cwd, "nx.json"), "{}");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);

        // Act
        var project = resolver.Resolve(null);

        // Assert
        Assert.Equal("workspace-root", project.Value);
    }

    [Fact]
    public void Resolve_CargoTomlWithoutWorkspaceTable_IsNotTreatedAsMarker()
    {
        // Arrange
        var grandparent = CreateDir("cargo-outer");
        File.WriteAllText(Path.Combine(grandparent, "nx.json"), "{}");
        var parent = CreateDir("cargo-outer", "cargo-mid");
        File.WriteAllText(Path.Combine(parent, "Cargo.toml"), "[package]\nname = \"demo\"\n");
        var cwd = CreateDir("cargo-outer", "cargo-mid", "cargo-app");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);

        // Act
        var project = resolver.Resolve(null);

        // Assert
        Assert.Equal("cargo-outer/cargo-app", project.Value);
    }

    [Fact]
    public void Resolve_CargoTomlWithWorkspaceTable_IsTreatedAsMarker()
    {
        // Arrange
        var root = CreateDir("cargo-ws-root");
        File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[workspace]\nmembers = [\"cargo-ws-app\"]\n");
        var cwd = CreateDir("cargo-ws-root", "cargo-ws-app");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);

        // Act
        var project = resolver.Resolve(null);

        // Assert
        Assert.Equal("cargo-ws-root/cargo-ws-app", project.Value);
    }

    [Fact]
    public void Resolve_PyprojectWithUvWorkspaceTable_IsTreatedAsMarker()
    {
        // Arrange
        var root = CreateDir("py-root");
        File.WriteAllText(Path.Combine(root, "pyproject.toml"), "[tool.uv.workspace]\nmembers = [\"py-app\"]\n");
        var cwd = CreateDir("py-root", "py-app");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);

        // Act
        var project = resolver.Resolve(null);

        // Assert
        Assert.Equal("py-root/py-app", project.Value);
    }

    [Fact]
    public void Resolve_InvalidCharsetDirectoryName_ThrowsAmbiguousProjectWithTried()
    {
        // Arrange
        var cwd = CreateDir("my app");
        File.WriteAllText(Path.Combine(cwd, "nx.json"), "{}");
        var resolver = new ProjectResolver(ConfigWith(null), cwd);
        Action act = () => resolver.Resolve(null);

        // Act / Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.AmbiguousProject, exception.Code);
        Assert.Contains("my app", exception.Tried);
    }

    private static VaultConfig ConfigWith(string projectOverride)
        => new()
        {
            Home = Path.GetTempPath(),
            DbPath = Path.Combine(Path.GetTempPath(), "vault.db"),
            FilesDir = Path.Combine(Path.GetTempPath(), "files"),
            MaxContentBytes = VaultConfig.DefaultMaxContentBytes,
            BusyTimeoutMs = VaultConfig.DefaultBusyTimeoutMs,
            ProjectOverride = projectOverride,
            PurgeDeleteFiles = false,
        };

    private string CreateDir(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }
}