using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Security;

public sealed class SecretDenylistTests
{
    private readonly SecretDenylist _denylist = new();

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.production")]
    [InlineData("config/.env.local")]
    [InlineData("production.env")]
    [InlineData("id_rsa")]
    [InlineData("keys/id_ed25519")]
    [InlineData("id_ecdsa.pub")]
    [InlineData("server.pem")]
    [InlineData("certs/tls.key")]
    [InlineData("bundle.pfx")]
    [InlineData("store.jks")]
    [InlineData("app.keystore")]
    [InlineData("session.ppk")]
    [InlineData(".netrc")]
    [InlineData("_netrc")]
    [InlineData(".git-credentials")]
    [InlineData(".npmrc")]
    [InlineData(".pypirc")]
    [InlineData(".htpasswd")]
    [InlineData("terraform.tfstate")]
    [InlineData("prod.tfvars")]
    [InlineData("secrets.json")]
    [InlineData("config/secrets.yaml")]
    [InlineData("db.credentials")]
    [InlineData("local.settings.json")]
    [InlineData("src/MyFunc/local.settings.json")]
    public void IsDeniedFile_SecretBasename_ReturnsTrue(string path)
    {
        // Arrange
        // Act
        var actual = _denylist.IsDeniedFile(path);

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData(".git/config")]
    [InlineData(".git/hooks/pre-commit")]
    [InlineData("sub/.git/config")]
    [InlineData(".ssh/id_rsa")]
    [InlineData(".ssh/known_hosts")]
    [InlineData(".aws/credentials")]
    [InlineData(".kube/config")]
    [InlineData(".docker/config.json")]
    [InlineData(".config/gcloud/credentials.db")]
    [InlineData(".hg/hgrc")]
    [InlineData(".svn/entries")]
    public void IsDeniedFile_InsideDeniedDirectory_ReturnsTrue(string path)
    {
        // Arrange
        // Act
        var actual = _denylist.IsDeniedFile(path);

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData("src/Program.cs")]
    [InlineData(".gitignore")]
    [InlineData("readme.md")]
    [InlineData(".config/app.json")]
    [InlineData("env.example")]
    [InlineData("docs/architecture.md")]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void IsDeniedFile_OrdinarySourceOrConfig_ReturnsFalse(string path)
    {
        // Arrange
        // Act
        var actual = _denylist.IsDeniedFile(path);

        // Assert
        Assert.False(actual);
    }

    [Theory]
    [InlineData(".GIT/config")]
    [InlineData("ID_RSA")]
    [InlineData("Server.PEM")]
    [InlineData(".SSH/known_hosts")]
    [InlineData("Local.Settings.JSON")]
    public void IsDeniedFile_IsAlwaysCaseInsensitive(string path)
    {
        // Arrange
        // Act
        var actual = _denylist.IsDeniedFile(path);

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData(".git", true)]
    [InlineData(".ssh", true)]
    [InlineData(".config/gcloud", true)]
    [InlineData("src", false)]
    [InlineData(".config", false)]
    [InlineData("node_modules", false)]
    public void IsDeniedDirectory_ClassifiesSecretDirectories(string path, bool expected)
    {
        // Arrange
        // Act
        var actual = _denylist.IsDeniedDirectory(path);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsDeniedFile_NullPath_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => _denylist.IsDeniedFile(null));
    }

    [Theory]
    [InlineData("build.secret")]
    [InlineData("nested/token.secret")]
    public void IsDeniedFile_ExtraFileGlob_ReturnsTrue(string path)
    {
        // Arrange
        var denylist = new SecretDenylist(["*.secret"]);

        // Act
        var actual = denylist.IsDeniedFile(path);

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void IsDeniedDirectory_ExtraDirectorySegment_ReturnsTrue()
    {
        // Arrange
        var denylist = new SecretDenylist(["private/"]);

        // Act
        // Assert
        Assert.True(denylist.IsDeniedDirectory("a/private"));
        Assert.True(denylist.IsDeniedFile("a/private/token.txt"));
    }

    [Fact]
    public void Constructor_ExtraDenyCannotRemoveBuiltin()
    {
        // Arrange
        var denylist = new SecretDenylist(["!*.env"]);

        // Act
        var actual = denylist.IsDeniedFile(".env");

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:/secrets")]
    [InlineData("\\\\host\\share")]
    public void Constructor_AbsoluteExtraEntry_Throws(string entry)
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentException>(() => new SecretDenylist([entry]));
    }

    [Fact]
    public void Constructor_ExtraDirectoryEntryWithInternalSlash_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentException>(() => new SecretDenylist(["a/b/"]));
    }

    [Fact]
    public void Constructor_MalformedExtraGlob_Throws()
    {
        // Arrange
        const string deeplyNested = "{{{{{a}}}}}";

        // Act
        // Assert
        Assert.Throws<RegexCompilationException>(() => new SecretDenylist([deeplyNested]));
    }

    [Fact]
    public void DescribePatterns_IncludesExtras()
    {
        // Arrange
        var denylist = new SecretDenylist(["*.secret", "private/"]);

        // Act
        var patterns = denylist.DescribePatterns();

        // Assert
        Assert.Contains("*.secret", patterns);
        Assert.Contains("**/private/**", patterns);
    }

    [Fact]
    public void ReparentUnsafeLeafSegments_ContainsConfigParent()
    {
        // Arrange
        // Act
        var segments = _denylist.ReparentUnsafeLeafSegments;

        // Assert
        Assert.Contains(".config", segments);
    }
}