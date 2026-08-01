using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Envelope;

public class ResultEnvelopeTests
{
    [Fact]
    public void Success_MirrorsResultsCountAndDefaultsErrorToNull()
    {
        // Act
        var envelope = ResultEnvelope.Success(
            new List<object> { 1, 2, 3 },
            repoRoot: "/repo");

        // Assert
        Assert.Equal(3, envelope.Count);
        Assert.Null(envelope.Error);
        Assert.Equal("/repo", envelope.RepoRoot);
        Assert.False(envelope.Truncated);
        Assert.Empty(envelope.FiltersApplied);
    }

    [Fact]
    public void Success_PreservesFiltersAndPreFilterCount()
    {
        // Arrange
        var filters = new Dictionary<string, object> { ["author"] = "<redacted>" };

        // Act
        var envelope = ResultEnvelope.Success(
            new List<object>(),
            repoRoot: "/repo",
            preFilterCount: 42,
            filtersApplied: filters,
            truncated: true);

        // Assert
        Assert.Equal(0, envelope.Count);
        Assert.Equal(42, envelope.PreFilterCount);
        Assert.True(envelope.Truncated);
        Assert.Equal("<redacted>", envelope.FiltersApplied["author"]);
    }

    [Fact]
    public void Failure_CarriesErrorCodeAndMessage()
    {
        // Arrange
        var ex = new RejectedArgumentException("bad", new Dictionary<string, object> { ["param"] = "ref" });

        // Act
        var envelope = ResultEnvelope.Failure(ex, repoRoot: "/repo");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("bad", envelope.Error.Message);
        Assert.Equal("ref", envelope.Error.Detail["param"]);
        Assert.Empty(envelope.Results);
        Assert.Equal(0, envelope.Count);
    }
}