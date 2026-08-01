using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public class GitGrepToolTests
{
    [Theory]
    [InlineData("fatal: cannot use Perl-compatible regexes when not compiled with USE_LIBPCRE")]
    [InlineData("fatal: cannot use Perl-compatible regexes when not compiled with USE_LIBPCRE2")]
    [InlineData("error: USE_LIBPCRE")]
    public void ClassifyPcreStderr_MapsPcreSignatureToPcreUnavailable(string stderr)
    {
        // Act
        var result = GitGrepTool.ClassifyPcreStderr(stderr);

        // Assert
        var exception = Assert.IsType<PcreUnavailableException>(result);
        Assert.Equal(ErrorCodes.PcreUnavailable, exception.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fatal: bad revision 'HEAD~99'")]
    [InlineData("fatal: ambiguous argument 'nope'")]
    public void ClassifyPcreStderr_ReturnsNullForUnrelatedStderr(string stderr)
    {
        // Act & Assert
        Assert.Null(GitGrepTool.ClassifyPcreStderr(stderr));
    }
}