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
}