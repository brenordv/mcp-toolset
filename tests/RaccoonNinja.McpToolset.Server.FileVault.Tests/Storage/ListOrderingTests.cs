using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Storage;

/// <summary>Tests for <see cref="ListOrdering"/>: the ordinal total order and the cursor skip predicate.</summary>
public sealed class ListOrderingTests
{
    [Fact]
    public void Comparer_NameTiebreak_IsOrdinalNotCulture()
    {
        // Arrange
        var zebra = Row(100, "Zebra", "p");
        var apple = Row(100, "apple", "p");

        // Act
        var ordered = new[] { apple, zebra }.OrderBy(row => row, ListOrdering.Comparer).Select(row => row.Name).ToList();

        // Assert
        Assert.Equal(["Zebra", "apple"], ordered);
    }

    [Fact]
    public void Comparer_NewerUpdatedAtSortsFirst()
    {
        // Arrange
        var older = Row(1, "a", "p");
        var newer = Row(2, "b", "p");

        // Act
        var ordered = new[] { older, newer }.OrderBy(row => row, ListOrdering.Comparer).Select(row => row.Name).ToList();

        // Assert
        Assert.Equal(["b", "a"], ordered);
    }

    [Fact]
    public void Comparer_ProjectTiebreak_IsOrdinalAscending()
    {
        // Arrange
        var a = Row(5, "same", "proj-a");
        var b = Row(5, "same", "proj-b");

        // Act
        var ordered = new[] { b, a }.OrderBy(row => row, ListOrdering.Comparer).Select(row => row.Project).ToList();

        // Assert
        Assert.Equal(["proj-a", "proj-b"], ordered);
    }

    [Fact]
    public void IsAfterCursor_TrueOnlyForRowsSortingStrictlyLater()
    {
        // Arrange
        var cursor = Row(100, "m", "p");
        var earlier = Row(200, "a", "p");
        var later = Row(50, "z", "p");

        // Act + Assert
        Assert.False(ListOrdering.IsAfterCursor(earlier, cursor.UpdatedAt, cursor.Name, cursor.Project));
        Assert.False(ListOrdering.IsAfterCursor(cursor, cursor.UpdatedAt, cursor.Name, cursor.Project));
        Assert.True(ListOrdering.IsAfterCursor(later, cursor.UpdatedAt, cursor.Name, cursor.Project));
    }

    private static FileSummaryRow Row(long updatedAt, string name, string project)
        => new() { Project = project, Name = name, UpdatedAt = updatedAt, CurrentVersion = 1, Summary = "s" };
}