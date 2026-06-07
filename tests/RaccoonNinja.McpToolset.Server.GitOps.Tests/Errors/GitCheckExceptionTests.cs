using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Errors;

public class GitCheckExceptionTests
{
    [Fact]
    public void Base_Carries_Message_And_Default_Code()
    {
        var ex = new GitCheckException("boom");
        Assert.Equal("boom", ex.Message);
        Assert.Equal(ErrorCodes.GitCheckError, ex.Code);
        Assert.Empty(ex.Detail);
    }

    [Fact]
    public void Detail_Is_Independent_Copy_From_Caller()
    {
        var detail = new Dictionary<string, object> { ["k"] = 1 };
        var ex = new GitCheckException("boom", detail);
        Assert.Equal(1, ex.Detail["k"]);
    }

    [Theory]
    [InlineData(typeof(GitNotInstalledException), ErrorCodes.GitNotInstalled)]
    [InlineData(typeof(NotAGitRepositoryException), ErrorCodes.NotAGitRepository)]
    [InlineData(typeof(RefNotFoundException), ErrorCodes.RefNotFound)]
    [InlineData(typeof(AmbiguousRefException), ErrorCodes.AmbiguousRef)]
    [InlineData(typeof(PathNotFoundException), ErrorCodes.PathNotFound)]
    [InlineData(typeof(PathOutsideRepoException), ErrorCodes.PathOutsideRepo)]
    [InlineData(typeof(RejectedArgumentException), ErrorCodes.RejectedArgument)]
    [InlineData(typeof(GitTimeoutException), ErrorCodes.GitTimeout)]
    [InlineData(typeof(GitCommandException), ErrorCodes.GitCommandError)]
    public void Subclasses_Expose_Stable_Codes(System.Type exType, string expected)
    {
        var instance = (GitCheckException)System.Activator.CreateInstance(exType, new object[] { "msg", null });
        Assert.Equal(expected, instance.Code);
    }
}