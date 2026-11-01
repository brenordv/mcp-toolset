using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public sealed class NulRecordsTests
{
    private const int FieldCount = 2;
    private const string NotWholeRecordsMessage = "git printed output that is not a whole number of NUL-framed records";

    [Fact]
    public void Split_WholeRecords_ReturnsEachRecordsFields()
    {
        // Arrange
        const string output = "a\0b\0c\0d\0";

        // Act
        var records = NulRecords.Split(Encoding.UTF8.GetBytes(output), FieldCount, truncated: false);

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(["a", "b"], records[0]);
        Assert.Equal(["c", "d"], records[1]);
    }

    [Fact]
    public void Split_EmptyOutput_ReturnsNoRecords()
    {
        // Act
        var records = NulRecords.Split([], FieldCount, truncated: false);

        // Assert
        Assert.Empty(records);
    }

    [Fact]
    public void Split_EmptyFields_KeepTheirPlace()
    {
        // Arrange
        const string output = "a\0\0\0d\0";

        // Act
        var records = NulRecords.Split(Encoding.UTF8.GetBytes(output), FieldCount, truncated: false);

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(["a", ""], records[0]);
        Assert.Equal(["", "d"], records[1]);
    }

    [Theory]
    [InlineData("a\0b\0c\0")]
    [InlineData("a\0b")]
    public void Split_OutputNotWholeRecords_Throws(string output)
    {
        // Act
        var act = () => NulRecords.Split(Encoding.UTF8.GetBytes(output), FieldCount, truncated: false);

        // Assert
        var exception = Assert.Throws<GitCommandException>(act);
        Assert.Equal(NotWholeRecordsMessage, exception.Message);
    }

    [Fact]
    public void Split_TruncatedOutput_PadsThePartialRecord()
    {
        // Arrange
        const string output = "a\0b\0c";

        // Act
        var records = NulRecords.Split(Encoding.UTF8.GetBytes(output), FieldCount, truncated: true);

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(["a", "b"], records[0]);
        Assert.Equal(["c", ""], records[1]);
    }

    [Fact]
    public void Split_TruncatedOutputEndingOnARecord_AddsNoPartialRecord()
    {
        // Arrange
        const string output = "a\0b\0";

        // Act
        var records = NulRecords.Split(Encoding.UTF8.GetBytes(output), FieldCount, truncated: true);

        // Assert
        var record = Assert.Single(records);
        Assert.Equal(["a", "b"], record);
    }

    [Fact]
    public void SplitNewlineTerminated_WholeRecords_ReturnsEachRecordsFields()
    {
        // Arrange
        const string output = "a\0b\0\nc\0d\0\n";

        // Act
        var records = NulRecords.SplitNewlineTerminated(Encoding.UTF8.GetBytes(output), FieldCount, truncated: false);

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(["a", "b"], records[0]);
        Assert.Equal(["c", "d"], records[1]);
    }

    [Fact]
    public void SplitNewlineTerminated_FieldStartingWithNewline_KeepsTheNewline()
    {
        // Arrange
        const string output = "a\0\nb\0\n";

        // Act
        var records = NulRecords.SplitNewlineTerminated(Encoding.UTF8.GetBytes(output), FieldCount, truncated: false);

        // Assert
        var record = Assert.Single(records);
        Assert.Equal(["a", "\nb"], record);
    }

    [Fact]
    public void SplitNewlineTerminated_EmptyOutput_ReturnsNoRecords()
    {
        // Act
        var records = NulRecords.SplitNewlineTerminated([], FieldCount, truncated: false);

        // Assert
        Assert.Empty(records);
    }

    [Theory]
    [InlineData("a\0b\0")]
    [InlineData("a\0b\0c\0d\0\n")]
    [InlineData("a\0b\0\nc\0")]
    public void SplitNewlineTerminated_OutputNotWholeRecords_Throws(string output)
    {
        // Act
        var act = () => NulRecords.SplitNewlineTerminated(Encoding.UTF8.GetBytes(output), FieldCount, truncated: false);

        // Assert
        var exception = Assert.Throws<GitCommandException>(act);
        Assert.Equal(NotWholeRecordsMessage, exception.Message);
    }

    [Theory]
    [InlineData("a\0b\0c\0d\0\n")]
    [InlineData("a\0b\0c")]
    public void SplitNewlineTerminated_TruncatedOutputMissingANewline_Throws(string output)
    {
        // Act
        var act = () => NulRecords.SplitNewlineTerminated(Encoding.UTF8.GetBytes(output), FieldCount, truncated: true);

        // Assert
        var exception = Assert.Throws<GitCommandException>(act);
        Assert.Equal(NotWholeRecordsMessage, exception.Message);
    }

    [Fact]
    public void SplitNewlineTerminated_TruncatedOutput_PadsThePartialRecord()
    {
        // Arrange
        const string output = "a\0b\0\nc";

        // Act
        var records = NulRecords.SplitNewlineTerminated(Encoding.UTF8.GetBytes(output), FieldCount, truncated: true);

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(["a", "b"], records[0]);
        Assert.Equal(["c", ""], records[1]);
    }

    [Fact]
    public void SplitNewlineTerminated_TruncatedBeforeTheNewline_AddsNoPartialRecord()
    {
        // Arrange
        const string output = "a\0b\0";

        // Act
        var records = NulRecords.SplitNewlineTerminated(Encoding.UTF8.GetBytes(output), FieldCount, truncated: true);

        // Assert
        var record = Assert.Single(records);
        Assert.Equal(["a", "b"], record);
    }
}