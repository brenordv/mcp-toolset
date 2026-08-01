using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>One walked file in the scoped result window, ordered by its scope-relative path.</summary>
/// <param name="Entry">The walked file entry.</param>
internal sealed record FlatFile(WalkEntry Entry);