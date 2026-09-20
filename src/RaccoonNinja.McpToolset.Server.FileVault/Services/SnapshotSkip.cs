namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// A note whose snapshot could not be read during <c>vault_search</c>. All fields are loggable
/// identifiers; carried out of the service so the tool can log the store-integrity warning without
/// the service holding a logger.
/// </summary>
public sealed record SnapshotSkip
{
    /// <summary>The skipped note's project.</summary>
    public string Project { get; init; }

    /// <summary>The skipped note's name.</summary>
    public string Name { get; init; }

    /// <summary>The type name of the read failure.</summary>
    public string ExceptionType { get; init; }
}