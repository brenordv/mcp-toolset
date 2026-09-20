namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// Which matching pass produced a query result. Values are explicit so an unset
/// <c>default</c> stays distinguishable from a real mode.
/// </summary>
public enum ListMode
{
    /// <summary>Every query term matched (the strict, precise pass).</summary>
    AllTerms = 1,

    /// <summary>The strict pass matched nothing, so a ranked any-term pass ran instead.</summary>
    AnyTermFallback = 2,
}