using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Repo;

public class RefRangeTests
{
    [Theory]
    [InlineData("HEAD~2..HEAD", "HEAD~2", "..", "HEAD")]
    [InlineData("master...HEAD", "master", "...", "HEAD")]
    [InlineData("v1.2.3..v1.2.4", "v1.2.3", "..", "v1.2.4")]
    [InlineData("v1.2.3...v1.2.4", "v1.2.3", "...", "v1.2.4")]
    [InlineData("..HEAD", "", "..", "HEAD")]
    [InlineData("master..", "master", "..", "")]
    [InlineData("...B", "", "...", "B")]
    [InlineData("A...", "A", "...", "")]
    public void Parse_Splits_Valid_Range_Into_Sides_And_Operator(string input, string left, string op, string right)
    {
        var range = RefRange.Parse(input);

        Assert.NotNull(range);
        Assert.Equal(left, range.Value.Left);
        Assert.Equal(op, range.Value.Operator);
        Assert.Equal(right, range.Value.Right);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("abc123")]
    [InlineData("v1.2.3")]
    [InlineData("feature/thing")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Returns_Null_For_Plain_Ref_Or_Blank(string input)
    {
        var range = RefRange.Parse(input);

        Assert.Null(range);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("A....B")]
    [InlineData("A..B..C")]
    [InlineData("A...B...C")]
    public void Parse_Throws_For_Malformed_Range(string input)
    {
        Assert.Throws<RejectedArgumentException>(() => RefRange.Parse(input));
    }

    [Fact]
    public void Parse_Malformed_Range_Rejection_Names_Ref_Param_Without_Echoing_Value()
    {
        var ex = Assert.Throws<RejectedArgumentException>(() => RefRange.Parse("secret....branch"));

        Assert.Equal("ref", ex.Detail["param"]);
        Assert.DoesNotContain("secret", ex.Message);
    }
}