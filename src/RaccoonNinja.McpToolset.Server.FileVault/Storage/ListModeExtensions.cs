namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>Wire-string conversion for <see cref="ListMode"/>.</summary>
public static class ListModeExtensions
{
    /// <summary>The snake_case <c>query_mode</c> value reported to the client.</summary>
    /// <param name="mode">The matching mode.</param>
    /// <returns>The wire string.</returns>
    public static string ToWireString(this ListMode mode)
        => mode switch
        {
            ListMode.AnyTermFallback => "any_term_fallback",
            _ => "all_terms",
        };
}