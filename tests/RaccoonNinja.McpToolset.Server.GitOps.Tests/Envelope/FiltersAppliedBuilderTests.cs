using RaccoonNinja.McpToolset.Server.GitOps.Envelope;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Envelope;

public class FiltersAppliedBuilderTests
{
    private const string SecretValue = "super-secret-pattern";

    [Fact]
    public void Redact_MasksPresentValueAndOmitsBlank()
    {
        // Act
        var filters = FiltersAppliedBuilder.Create()
            .Redact("pattern", SecretValue)
            .Redact("ref", null)
            .Redact("author", string.Empty)
            .Build();

        // Assert
        Assert.Equal(FiltersAppliedBuilder.RedactedToken, filters["pattern"]);
        Assert.False(filters.ContainsKey("ref"));
        Assert.False(filters.ContainsKey("author"));
    }

    [Fact]
    public void Redact_NeverEmitsTheRawUserValue()
    {
        // Act
        var filters = FiltersAppliedBuilder.Create()
            .Redact("pattern", SecretValue)
            .Build();

        // Assert
        Assert.NotEqual(SecretValue, filters["pattern"]);
        Assert.Equal(FiltersAppliedBuilder.RedactedToken, filters["pattern"]);
    }

    [Fact]
    public void Flag_AndNumberAreAlwaysIncludedWithActualValue()
    {
        // Act
        var filters = FiltersAppliedBuilder.Create()
            .Flag("ignore_case", true)
            .Number("paths_count", 0)
            .Build();

        // Assert
        Assert.Equal(true, filters["ignore_case"]);
        Assert.Equal(0, filters["paths_count"]);
    }

    [Fact]
    public void Optional_IncludesNonNullAndOmitsNull()
    {
        // Arrange
        var range = new[] { 1, 5 };

        // Act
        var filters = FiltersAppliedBuilder.Create()
            .Optional("line_range", range)
            .Optional("missing", null)
            .Build();

        // Assert
        Assert.Same(range, filters["line_range"]);
        Assert.False(filters.ContainsKey("missing"));
    }
}