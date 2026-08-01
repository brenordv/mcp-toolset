using NSubstitute;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Extensions;

public class RefVerifierExtensionsTests
{
    private const string Root = "/repo";

    [Fact]
    public async Task VerifyOptionalRefsAsync_SkipsNullAndEmptyRefs()
    {
        // Arrange
        var verifier = Substitute.For<IRefVerifier>();
        verifier.VerifyAsync("HEAD", Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha-head"));

        // Act
        var result = await verifier.VerifyOptionalRefsAsync(Root, ["HEAD", null, string.Empty]);

        // Assert
        Assert.Equal(["sha-head"], result);
        await verifier.Received(1).VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyOptionalRefsAsync_ReturnsEmptyForNullList()
    {
        // Arrange
        var verifier = Substitute.For<IRefVerifier>();

        // Act
        var result = await verifier.VerifyOptionalRefsAsync(Root, null);

        // Assert
        Assert.Empty(result);
        await verifier.DidNotReceive().VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyRequiredRefAsync_NormalizesNullToEmptyAndCallsVerifier()
    {
        // Arrange
        var verifier = Substitute.For<IRefVerifier>();
        verifier.VerifyAsync(string.Empty, Root, Arg.Any<CancellationToken>()).Returns(Task.FromResult("sha"));

        // Act
        var sha = await verifier.VerifyRequiredRefAsync(Root, null);

        // Assert
        Assert.Equal("sha", sha);
        await verifier.Received(1).VerifyAsync(string.Empty, Root, Arg.Any<CancellationToken>());
    }
}