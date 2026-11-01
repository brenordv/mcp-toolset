using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class PathspecValidationTests
{
    private const string ParamName = "globs";
    private const string PathsParam = "paths";

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(":(top)x")]
    [InlineData(":!x")]
    [InlineData("src\\x")]
    [InlineData("/abs")]
    [InlineData("C:x")]
    [InlineData("//share/x")]
    [InlineData("../x")]
    [InlineData("a/../b")]
    [InlineData("-x")]
    public void ValidatePattern_RejectsUnsafeShapes(string value)
    {
        // Act & Assert
        var exception = Assert.Throws<RejectedArgumentException>(() => PathspecValidation.ValidatePattern(ParamName, value));
        Assert.Equal(ParamName, exception.Detail["param"]);
        Assert.Contains(ParamName, exception.Message);
    }

    [Theory]
    [InlineData("*.cs")]
    [InlineData("crates/*/*/Cargo.toml")]
    [InlineData("a[0-9].txt")]
    [InlineData("./src/*.cs")]
    [InlineData("dir")]
    public void ValidatePattern_AcceptsRepoRelativePatterns(string value)
    {
        // Act
        var result = PathspecValidation.ValidatePattern(ParamName, value);

        // Assert
        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData("*.cs")]
    [InlineData("a?.txt")]
    [InlineData(":(exclude)x")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void EnsureLiteral_RejectsWildcardsMagicAndEmpty(string value)
    {
        // Act & Assert
        var exception = Assert.Throws<RejectedArgumentException>(() => PathspecValidation.EnsureLiteral(value));
        Assert.Equal(PathsParam, exception.Detail["param"]);
    }

    [Theory]
    [InlineData("src/a.cs")]
    [InlineData("app/[slug]/page.tsx")]
    public void EnsureLiteral_PassesLiteralPathsThrough(string value)
    {
        // Act
        var result = PathspecValidation.EnsureLiteral(value);

        // Assert
        Assert.Equal(value, result);
    }
}