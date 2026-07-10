using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Exceptions;
using RaccoonNinja.McpToolset.Server.FileVault.Security;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Configuration;

/// <summary>Serializes every test that mutates process-global environment variables.</summary>
[CollectionDefinition("EnvironmentVariables")]
public sealed class EnvironmentVariablesCollection
{
}

/// <summary>Tests for <see cref="VaultConfig.WithHome"/>: defaults, env overrides, and fatal misconfiguration.</summary>
[Collection("EnvironmentVariables")]
public sealed class VaultConfigTests
{
    [Fact]
    public void WithHome_NoOverridesSet_UsesDocumentedDefaults()
    {
        // Arrange
        using var scope = new VaultEnvScope();
        var home = TempHome();

        // Act
        var config = VaultConfig.WithHome(home);

        // Assert
        Assert.Equal(home, config.Home);
        Assert.Equal(Path.Combine(home, "vault.db"), config.DbPath);
        Assert.Equal(Path.Combine(home, "files"), config.FilesDir);
        Assert.Equal(10_485_760L, config.MaxContentBytes);
        Assert.Equal(5_000, config.BusyTimeoutMs);
        Assert.Equal(14_000, config.SplitHintChars);
        Assert.False(config.PurgeDeleteFiles);
        Assert.Null(config.ProjectOverride);
    }

    [Fact]
    public void WithHome_MaxBytesSet_ParsesValue()
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_MAX_BYTES", "1048576");

        // Act
        var config = VaultConfig.WithHome(TempHome());

        // Assert
        Assert.Equal(1_048_576L, config.MaxContentBytes);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public void WithHome_MaxBytesInvalid_ThrowsVaultStartupException(string raw)
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_MAX_BYTES", raw);
        Action act = () => VaultConfig.WithHome(TempHome());

        // Act / Assert
        var ex = Assert.Throws<VaultStartupException>(act);
        Assert.Contains("not a valid byte count", ex.Message);
    }

    [Fact]
    public void WithHome_BusyTimeoutSet_ParsesValue()
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_BUSY_TIMEOUT_MS", "250");

        // Act
        var config = VaultConfig.WithHome(TempHome());

        // Assert
        Assert.Equal(250, config.BusyTimeoutMs);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public void WithHome_BusyTimeoutInvalid_ThrowsVaultStartupException(string raw)
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_BUSY_TIMEOUT_MS", raw);
        Action act = () => VaultConfig.WithHome(TempHome());

        // Act / Assert
        var ex = Assert.Throws<VaultStartupException>(act);
        Assert.Contains("not a valid millisecond count", ex.Message);
    }

    [Theory]
    [InlineData("500", 500)]
    [InlineData("0", 0)]
    public void WithHome_SplitHintCharsSet_ParsesValue(string raw, int expected)
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_SPLIT_HINT_CHARS", raw);

        // Act
        var config = VaultConfig.WithHome(TempHome());

        // Assert
        Assert.Equal(expected, config.SplitHintChars);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public void WithHome_SplitHintCharsInvalid_ThrowsVaultStartupException(string raw)
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_SPLIT_HINT_CHARS", raw);
        Action act = () => VaultConfig.WithHome(TempHome());

        // Act / Assert
        var ex = Assert.Throws<VaultStartupException>(act);
        Assert.Contains("not a valid character count", ex.Message);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("Yes")]
    [InlineData(" on ")]
    [InlineData("  YES  ")]
    public void WithHome_PurgeDeleteFilesTruthy_ReturnsTrue(string raw)
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_PURGE_DELETE_FILES", raw);

        // Act
        var config = VaultConfig.WithHome(TempHome());

        // Assert
        Assert.True(config.PurgeDeleteFiles);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("No")]
    [InlineData(" off ")]
    [InlineData("  OFF  ")]
    public void WithHome_PurgeDeleteFilesFalsy_ReturnsFalse(string raw)
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_PURGE_DELETE_FILES", raw);

        // Act
        var config = VaultConfig.WithHome(TempHome());

        // Assert
        Assert.False(config.PurgeDeleteFiles);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithHome_PurgeDeleteFilesEmptyOrWhitespace_UsesDefault(string raw)
    {
        // Arrange
        // Note: setting an env var to "" removes it on .NET, so the "" case exercises the
        // unset path while "   " exercises the trimmed-to-empty switch arm.
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_PURGE_DELETE_FILES", raw);

        // Act
        var config = VaultConfig.WithHome(TempHome());

        // Assert
        Assert.False(config.PurgeDeleteFiles);
    }

    [Fact]
    public void WithHome_PurgeDeleteFilesUnparseable_ThrowsVaultStartupException()
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_PURGE_DELETE_FILES", "maybe");
        Action act = () => VaultConfig.WithHome(TempHome());

        // Act / Assert
        var ex = Assert.Throws<VaultStartupException>(act);
        Assert.Contains("not a valid boolean", ex.Message);
    }

    [Fact]
    public void WithHome_ProjectSetWithPadding_IsTrimmed()
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_PROJECT", "  my-project  ");

        // Act
        var config = VaultConfig.WithHome(TempHome());

        // Assert
        Assert.Equal("my-project", config.ProjectOverride);
    }

    [Fact]
    public void WithHome_ProjectWhitespaceOnly_ProjectOverrideIsNull()
    {
        // Arrange
        using var scope = new VaultEnvScope();
        VaultEnvScope.Set("VAULT_MCP_PROJECT", "   ");

        // Act
        var config = VaultConfig.WithHome(TempHome());

        // Assert
        Assert.Null(config.ProjectOverride);
    }

    [Fact]
    public void WithHome_AnyHome_DerivesDbPathAndFilesDirFromHome()
    {
        // Arrange
        using var scope = new VaultEnvScope();
        var home = TempHome();

        // Act
        var config = VaultConfig.WithHome(home);

        // Assert
        Assert.Equal(Path.Combine(home, "vault.db"), config.DbPath);
        Assert.Equal(Path.Combine(home, "files"), config.FilesDir);
    }

    private static string TempHome()
        => Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));
}

/// <summary>Tests for <see cref="EnvironmentBuilder.Build"/>: the hardened allowlist child environment.</summary>
[Collection("EnvironmentVariables")]
public sealed class EnvironmentBuilderTests
{
    [Fact]
    public void Build_Called_AlwaysSetsGitNeutralizers()
    {
        // Arrange / Act
        var env = EnvironmentBuilder.Build();

        // Assert
        Assert.Equal("0", env["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", env["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("0", env["GIT_OPTIONAL_LOCKS"]);
    }

    [Fact]
    public void Build_NonAllowlistedVariableSet_DropsIt()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable("GIT_SSH_COMMAND");
        Environment.SetEnvironmentVariable("GIT_SSH_COMMAND", "evil");
        try
        {
            // Act
            var env = EnvironmentBuilder.Build();

            // Assert
            Assert.False(env.ContainsKey("GIT_SSH_COMMAND"));
            Assert.True(env.ContainsKey("PATH"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_SSH_COMMAND", previous);
        }
    }
}

/// <summary>Snapshots and clears the VAULT_MCP_* variables, restoring the prior values on dispose.</summary>
internal sealed class VaultEnvScope : IDisposable
{
    private static readonly string[] Keys =
    [
        "VAULT_MCP_MAX_BYTES",
        "VAULT_MCP_BUSY_TIMEOUT_MS",
        "VAULT_MCP_SPLIT_HINT_CHARS",
        "VAULT_MCP_PURGE_DELETE_FILES",
        "VAULT_MCP_PROJECT",
    ];

    private readonly Dictionary<string, string> _saved = new();

    public VaultEnvScope()
    {
        foreach (var key in Keys)
        {
            _saved[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    public static void Set(string key, string value)
        => Environment.SetEnvironmentVariable(key, value);

    public void Dispose()
    {
        foreach (var pair in _saved)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}