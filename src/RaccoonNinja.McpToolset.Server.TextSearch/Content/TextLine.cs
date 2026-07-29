namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>
/// One line of a decoded text file: its 1-based number and its terminator-free content. A column is a
/// 1-based UTF-16 code-unit index into <see cref="Content"/>, which matches .NET string indexing and
/// the LSP default (a surrogate pair, for example an emoji, counts as two units).
/// </summary>
/// <param name="Number">The 1-based line number.</param>
/// <param name="Content">The line text with its trailing CR/LF removed.</param>
public sealed record TextLine(int Number, string Content);