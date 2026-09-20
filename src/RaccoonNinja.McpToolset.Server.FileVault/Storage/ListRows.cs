namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// The repository's <see cref="IVaultRepository.List"/> output: the matching rows plus the pass
/// that produced them. In <see cref="ListMode.AnyTermFallback"/> the rows are already fully
/// ordered (rank ascending, then the shared list ordering); in <see cref="ListMode.AllTerms"/>
/// the caller finalizes ordering with <c>ListOrdering</c>.
/// </summary>
public sealed record ListRows
{
    /// <summary>The matching summary rows.</summary>
    public IReadOnlyList<FileSummaryRow> Rows { get; init; } = [];

    /// <summary>Which matching pass produced <see cref="Rows"/>.</summary>
    public ListMode Mode { get; init; }
}