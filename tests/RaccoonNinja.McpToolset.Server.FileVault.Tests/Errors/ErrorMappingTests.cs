using System.Text.Json;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Errors;

/// <summary>Pins the error wire contract: the snake_case codes and the per-code error-body payload fields.</summary>
public class ErrorMappingTests
{
    [Theory]
    [InlineData(VaultErrorCode.NotFound, "not_found")]
    [InlineData(VaultErrorCode.Conflict, "conflict")]
    [InlineData(VaultErrorCode.Archived, "archived")]
    [InlineData(VaultErrorCode.InvalidName, "invalid_name")]
    [InlineData(VaultErrorCode.InvalidFormat, "invalid_format")]
    [InlineData(VaultErrorCode.TooLarge, "too_large")]
    [InlineData(VaultErrorCode.AmbiguousProject, "ambiguous_project")]
    [InlineData(VaultErrorCode.HeadingNotFound, "heading_not_found")]
    [InlineData(VaultErrorCode.AmbiguousHeading, "ambiguous_heading")]
    [InlineData(VaultErrorCode.KeyPathNotFound, "key_path_not_found")]
    [InlineData(VaultErrorCode.ConfirmationRequired, "confirmation_required")]
    [InlineData(VaultErrorCode.ParentNotFound, "parent_not_found")]
    [InlineData(VaultErrorCode.InvalidParent, "invalid_parent")]
    [InlineData(VaultErrorCode.NothingToUpdate, "nothing_to_update")]
    public void ToWireCode_EveryCode_ReturnsStableSnakeCaseString(VaultErrorCode code, string expected)
    {
        // Act + Assert: these strings match the Rust server byte-for-byte and must never drift.
        Assert.Equal(expected, code.ToWireCode());
    }

    [Fact]
    public void ToWireCode_CoversEveryEnumValue()
    {
        // Act + Assert: a newly added enum value without a wire string would fall through to
        // "internal", which the theory above cannot see — this guard makes the gap loud.
        foreach (var code in Enum.GetValues<VaultErrorCode>())
        {
            // Every domain code needs its own wire string.
            Assert.NotEqual("internal", code.ToWireCode());
        }
    }

    [Fact]
    public void ToErrorJson_NotFound_CarriesProjectAndName()
    {
        // Arrange
        var exception = VaultException.NotFound("proj", "ghost");

        // Act
        using var body = JsonDocument.Parse(ErrorMapping.ToErrorJson(exception));

        // Assert
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.Equal("proj", error.GetProperty("project").GetString());
        Assert.Equal("ghost", error.GetProperty("name").GetString());
    }

    [Fact]
    public void ToErrorJson_HintlessConflict_CarriesCurrentVersionOnly()
    {
        // Arrange
        var exception = VaultException.Conflict(currentVersion: 3);

        // Act
        using var body = JsonDocument.Parse(ErrorMapping.ToErrorJson(exception));

        // Assert
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("conflict", error.GetProperty("code").GetString());
        Assert.Equal(3, error.GetProperty("current_version").GetInt32());
        Assert.False(error.TryGetProperty("base_version", out _));
        Assert.False(error.TryGetProperty("diff", out _));
    }

    [Fact]
    public void ToErrorJson_ConflictWithHint_CarriesCurrentBaseAndDiff()
    {
        // Arrange
        var exception = VaultException.ConflictWithHint(currentVersion: 3, baseVersion: 1, diff: "-old\n+new");

        // Act
        using var body = JsonDocument.Parse(ErrorMapping.ToErrorJson(exception));

        // Assert
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("conflict", error.GetProperty("code").GetString());
        Assert.Equal(3, error.GetProperty("current_version").GetInt32());
        Assert.Equal(1, error.GetProperty("base_version").GetInt32());
        Assert.Equal("-old\n+new", error.GetProperty("diff").GetString());
    }

    [Fact]
    public void ToErrorJson_Archived_CarriesProjectAndName()
    {
        // Arrange
        var exception = VaultException.Archived("proj", "frozen");

        // Act
        using var body = JsonDocument.Parse(ErrorMapping.ToErrorJson(exception));

        // Assert
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("archived", error.GetProperty("code").GetString());
        Assert.Equal("proj", error.GetProperty("project").GetString());
        Assert.Equal("frozen", error.GetProperty("name").GetString());
    }

    [Fact]
    public void ToErrorJson_AmbiguousProject_CarriesTriedArray()
    {
        // Arrange
        var exception = VaultException.AmbiguousProject(["my app", "other dir"]);

        // Act
        using var body = JsonDocument.Parse(ErrorMapping.ToErrorJson(exception));

        // Assert
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("ambiguous_project", error.GetProperty("code").GetString());
        Assert.Equal(["my app", "other dir"], error.GetProperty("tried").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void ToErrorJson_ParentNotFound_CarriesProjectAndParent()
    {
        // Arrange
        var exception = VaultException.ParentNotFound("proj", "missing-parent");

        // Act
        using var body = JsonDocument.Parse(ErrorMapping.ToErrorJson(exception));

        // Assert
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("parent_not_found", error.GetProperty("code").GetString());
        Assert.Equal("proj", error.GetProperty("project").GetString());
        Assert.Equal("missing-parent", error.GetProperty("parent").GetString());
    }

    [Fact]
    public void ToErrorJson_CodeOnlyFactory_CarriesCodeAndMessageOnly()
    {
        // Arrange
        var exception = VaultException.NothingToUpdate();

        // Act
        using var body = JsonDocument.Parse(ErrorMapping.ToErrorJson(exception));

        // Assert: no extra payload fields for a code-only error.
        var error = body.RootElement.GetProperty("error");
        Assert.Equal(["code", "message"], error.EnumerateObject().Select(p => p.Name));
        Assert.Equal("nothing_to_update", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("message").GetString()));
    }

    [Fact]
    public void ToInternalErrorJson_ExposesTypeNameButNeverTheMessage()
    {
        // Arrange
        var exception = new InvalidOperationException("secret internal detail");

        // Act
        using var body = JsonDocument.Parse(ErrorMapping.ToInternalErrorJson(exception));

        // Assert
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("internal", error.GetProperty("code").GetString());
        Assert.Contains(nameof(InvalidOperationException), error.GetProperty("message").GetString());
        Assert.DoesNotContain("secret internal detail", error.GetProperty("message").GetString());
    }
}