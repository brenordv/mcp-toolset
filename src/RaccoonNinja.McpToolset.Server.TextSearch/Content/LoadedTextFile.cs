using RaccoonNinja.McpToolset.Common.Files.Text;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>
/// One file loaded through the read gate: the decoded document plus the confined root-relative path
/// the gate resolved. Payload echoes always use <see cref="RelativePath"/>, never the raw path
/// argument, which may legitimately be an in-root absolute path that must not reach model context.
/// </summary>
/// <param name="Document">The decoded text document.</param>
/// <param name="RelativePath">The <c>/</c>-separated root-relative path the gate resolved.</param>
public sealed record LoadedTextFile(TextDocument Document, string RelativePath);