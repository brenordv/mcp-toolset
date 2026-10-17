using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Repo;

public sealed class RevPathExpressionTests
{
    [Theory]
    [InlineData("HEAD:README.md")]
    [InlineData("master:a/b.txt")]
    [InlineData("abc123~1:x")]
    [InlineData(":README.md")]
    [InlineData(":0:x")]
    [InlineData("HEAD@{1}:file")]
    [InlineData("HEAD^{commit}:file")]
    [InlineData("HEAD:")]
    public void IsRevPath_PathExpression_IsTrue(string reference)
    {
        // Act
        var actual = RevPathExpression.IsRevPath(reference);

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("v1.2.3")]
    [InlineData("main..feature")]
    [InlineData(":/fix bug")]
    [InlineData("HEAD@{2024-01-01 10:00:00}")]
    [InlineData("master@{yesterday}")]
    [InlineData("HEAD^{/fix: bug}")]
    [InlineData("HEAD@{")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void IsRevPath_PlainCommitIsh_IsFalse(string reference)
    {
        // Act
        var actual = RevPathExpression.IsRevPath(reference);

        // Assert
        Assert.False(actual);
    }
}