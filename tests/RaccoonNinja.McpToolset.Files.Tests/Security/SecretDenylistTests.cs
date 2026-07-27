using RaccoonNinja.McpToolset.Files.Security;

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
}