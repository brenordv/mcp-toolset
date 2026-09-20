using System.Buffers.Text;
using System.Text;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Services;

/// <summary>
/// Tests for <see cref="ListCursor"/>: round-trip, the length gate, and the defensive decode that
/// turns every defect into a <c>invalid_argument</c> rather than a raw parser exception.
/// </summary>
public sealed class ListCursorTests
{
    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        // Arrange
        var row = Row(1_700_000_000, "my-note", "proj/app");

        // Act
        var key = ListCursor.Decode(ListCursor.Encode(row));

        // Assert
        Assert.Equal(1_700_000_000, key.UpdatedAt);
        Assert.Equal("proj/app", key.Project);
        Assert.Equal("my-note", key.Name);
    }

    [Fact]
    public void EncodeDecode_MaxLengthProjectAndName_StayUnderLengthGate()
    {
        // Arrange
        var project = new string('p', 512);
        var name = new string('n', 128);

        // Act
        var cursor = ListCursor.Encode(Row(9_999, name, project));
        var key = ListCursor.Decode(cursor);

        // Assert
        Assert.True(cursor.Length <= VaultConfig.MaxCursorChars);
        Assert.Equal(project, key.Project);
        Assert.Equal(name, key.Name);
    }

    [Fact]
    public void Decode_Null_ThrowsInvalidArgument()
        => AssertMalformed(() => ListCursor.Decode(null));

    [Fact]
    public void Decode_Oversized_ThrowsInvalidArgumentBeforeDecoding()
        => AssertMalformed(() => ListCursor.Decode(new string('A', VaultConfig.MaxCursorChars + 1)));

    [Fact]
    public void Decode_TamperedBase64_ThrowsInvalidArgumentNotFormatException()
        => AssertMalformed(() => ListCursor.Decode("###not base64###"));

    [Fact]
    public void Decode_EmptyString_ThrowsInvalidArgumentNotJsonException()
        => AssertMalformed(() => ListCursor.Decode(string.Empty));

    [Fact]
    public void Decode_WrongVersion_ThrowsInvalidArgument()
        => AssertMalformed(() => ListCursor.Decode(EncodeRaw("{\"v\":2,\"u\":1,\"p\":\"x\",\"n\":\"y\"}")));

    [Fact]
    public void Decode_MissingField_ThrowsInvalidArgument()
        => AssertMalformed(() => ListCursor.Decode(EncodeRaw("{\"v\":1,\"u\":1,\"p\":\"x\"}")));

    [Fact]
    public void Decode_MistypedField_ThrowsInvalidArgument()
        => AssertMalformed(() => ListCursor.Decode(EncodeRaw("{\"v\":1,\"u\":\"not-a-number\",\"p\":\"x\",\"n\":\"y\"}")));

    [Fact]
    public void Decode_SkipPast_AgreesWithListOrderingOnBoundary()
    {
        // Arrange
        var boundary = Row(100, "m", "p");
        var key = ListCursor.Decode(ListCursor.Encode(boundary));
        var before = Row(100, "a", "p");
        var after = Row(100, "z", "p");

        // Act + Assert
        Assert.False(ListOrdering.IsAfterCursor(before, key.UpdatedAt, key.Name, key.Project));
        Assert.False(ListOrdering.IsAfterCursor(boundary, key.UpdatedAt, key.Name, key.Project));
        Assert.True(ListOrdering.IsAfterCursor(after, key.UpdatedAt, key.Name, key.Project));
    }

    private static void AssertMalformed(Action act)
    {
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidArgument, ex.Code);
    }

    private static string EncodeRaw(string json)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

    private static FileSummaryRow Row(long updatedAt, string name, string project)
        => new() { Project = project, Name = name, UpdatedAt = updatedAt, CurrentVersion = 1, Summary = "s" };
}