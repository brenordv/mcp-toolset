using NSubstitute;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Extensions;

public class RefVerifierExtensionsTests
{
    private const string Root = "/repo";

    [Fact]
    public async Task VerifyOptionalRefsAsync_Skips_Null_And_Empty_Refs()
    {
        var verifier = Substitute.For<IRefVerifier>();
        verifier.VerifyAsync("HEAD", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-head"));

        var result = await verifier.VerifyOptionalRefsAsync(Root, ["HEAD", null, string.Empty]);

        Assert.Equal(["sha-head"], result);
        await verifier.Received(1).VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyOptionalRefsAsync_Returns_Empty_For_Null_List()
    {
        var verifier = Substitute.For<IRefVerifier>();

        var result = await verifier.VerifyOptionalRefsAsync(Root, null);

        Assert.Empty(result);
        await verifier.DidNotReceive().VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyRequiredRefAsync_Normalizes_Null_To_Empty_And_Calls_Verifier()
    {
        var verifier = Substitute.For<IRefVerifier>();
        verifier.VerifyAsync(string.Empty, Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha"));

        var sha = await verifier.VerifyRequiredRefAsync(Root, null);

        Assert.Equal("sha", sha);
        await verifier.Received(1).VerifyAsync(string.Empty, Root, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyRefOrRangeAsync_Resolves_Plain_Ref_As_Single_Sha()
    {
        var verifier = Substitute.For<IRefVerifier>();
        verifier.VerifyAsync("HEAD", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-head"));

        var token = await verifier.VerifyRefOrRangeAsync(Root, "HEAD");

        Assert.Equal("sha-head", token);
        await verifier.Received(1).VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("A..B", "..")]
    [InlineData("A...B", "...")]
    public async Task VerifyRefOrRangeAsync_Resolves_Each_Side_And_Rejoins_With_Operator(string input, string op)
    {
        var verifier = Substitute.For<IRefVerifier>();
        verifier.VerifyAsync("A", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-a"));
        verifier.VerifyAsync("B", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-b"));

        var token = await verifier.VerifyRefOrRangeAsync(Root, input);

        Assert.Equal($"sha-a{op}sha-b", token);
    }

    [Fact]
    public async Task VerifyRefOrRangeAsync_Defaults_Empty_Side_To_Head()
    {
        var verifier = Substitute.For<IRefVerifier>();
        verifier.VerifyAsync("HEAD", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-head"));
        verifier.VerifyAsync("B", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-b"));

        var token = await verifier.VerifyRefOrRangeAsync(Root, "..B");

        Assert.Equal("sha-head..sha-b", token);
        await verifier.Received(1).VerifyAsync("HEAD", Root, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyOptionalRefsOrRangesAsync_Skips_Blanks_And_Verifies_Ranges()
    {
        var verifier = Substitute.For<IRefVerifier>();
        verifier.VerifyAsync("A", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-a"));
        verifier.VerifyAsync("B", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-b"));
        verifier.VerifyAsync("HEAD", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-head"));

        var result = await verifier.VerifyOptionalRefsOrRangesAsync(Root, ["A..B", null, string.Empty, "HEAD"]);

        Assert.Equal(["sha-a..sha-b", "sha-head"], result);
    }

    [Fact]
    public async Task VerifyOptionalRefsOrRangesAsync_Returns_Empty_For_Null_List()
    {
        var verifier = Substitute.For<IRefVerifier>();

        var result = await verifier.VerifyOptionalRefsOrRangesAsync(Root, null);

        Assert.Empty(result);
        await verifier.DidNotReceive().VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}