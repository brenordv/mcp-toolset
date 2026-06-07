using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Envelope;

public class ResultEnvelopeTests
{
    [Fact]
    public void Success_Mirrors_Results_Count_And_Defaults_Error_To_Null()
    {
        var envelope = ResultEnvelope.Success(
            new List<object> { 1, 2, 3 },
            repoRoot: "/repo");

        Assert.Equal(3, envelope.Count);
        Assert.Null(envelope.Error);
        Assert.Equal("/repo", envelope.RepoRoot);
        Assert.False(envelope.Truncated);
        Assert.Empty(envelope.FiltersApplied);
    }

    [Fact]
    public void Success_Preserves_Filters_And_PreFilterCount()
    {
        var filters = new Dictionary<string, object> { ["author"] = "<redacted>" };
        var envelope = ResultEnvelope.Success(
            new List<object>(),
            repoRoot: "/repo",
            preFilterCount: 42,
            filtersApplied: filters,
            truncated: true);

        Assert.Equal(0, envelope.Count);
        Assert.Equal(42, envelope.PreFilterCount);
        Assert.True(envelope.Truncated);
        Assert.Equal("<redacted>", envelope.FiltersApplied["author"]);
    }

    [Fact]
    public void Failure_Carries_Error_Code_And_Message()
    {
        var ex = new RejectedArgumentException("bad", new Dictionary<string, object> { ["param"] = "ref" });
        var envelope = ResultEnvelope.Failure(ex, repoRoot: "/repo");

        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("bad", envelope.Error.Message);
        Assert.Equal("ref", envelope.Error.Detail["param"]);
        Assert.Empty(envelope.Results);
        Assert.Equal(0, envelope.Count);
    }
}