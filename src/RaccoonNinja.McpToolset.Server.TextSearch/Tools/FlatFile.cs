using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>One file in the flattened cross-root window: its root (index within the target set + name) and entry.</summary>
/// <param name="RootIndex">The root's index within the resolved target set (drives the composite key ordering).</param>
/// <param name="RootName">The root's name surfaced in results.</param>
/// <param name="Entry">The walked file entry.</param>
internal sealed record FlatFile(int RootIndex, string RootName, WalkEntry Entry);