using RaccoonNinja.McpToolset.Server.GitOps.Envelope;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Envelope;

public class FiltersAppliedBuilderTests
{
    private const string SecretValue = "super-secret-pattern";

    [Fact]
    public void Redact_Masks_Present_Value_And_Omits_Blank()
    {
        var filters = FiltersAppliedBuilder.Create()
            .Redact("pattern", SecretValue)
            .Redact("ref", null)
            .Redact("author", string.Empty)
            .Build();

        Assert.Equal(FiltersAppliedBuilder.RedactedToken, filters["pattern"]);
        Assert.False(filters.ContainsKey("ref"));
        Assert.False(filters.ContainsKey("author"));
    }

    [Fact]
    public void Redact_Never_Emits_The_Raw_User_Value()
    {
        var filters = FiltersAppliedBuilder.Create()
            .Redact("pattern", SecretValue)
            .Build();

        Assert.NotEqual(SecretValue, filters["pattern"]);
        Assert.Equal(FiltersAppliedBuilder.RedactedToken, filters["pattern"]);
    }

    [Fact]
    public void Flag_And_Number_Are_Always_Included_With_Actual_Value()
    {
        var filters = FiltersAppliedBuilder.Create()
            .Flag("ignore_case", true)
            .Number("paths_count", 0)
            .Build();

        Assert.Equal(true, filters["ignore_case"]);
        Assert.Equal(0, filters["paths_count"]);
    }

    [Fact]
    public void Optional_Includes_NonNull_And_Omits_Null()
    {
        var range = new[] { 1, 5 };
        var filters = FiltersAppliedBuilder.Create()
            .Optional("line_range", range)
            .Optional("missing", null)
            .Build();

        Assert.Same(range, filters["line_range"]);
        Assert.False(filters.ContainsKey("missing"));
    }
}