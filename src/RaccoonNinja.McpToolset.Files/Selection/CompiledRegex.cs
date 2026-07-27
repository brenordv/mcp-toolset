using System.Text.RegularExpressions;

namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// A pattern compiled through <see cref="SafeRegexCompiler"/>: the ready-to-use <see cref="Regex"/> plus
/// whether it runs on the linear-time non-backtracking engine. When <see cref="IsNonBacktracking"/> is
/// <c>false</c> the pattern used a construct the non-backtracking engine cannot handle (a lookaround,
/// backreference, and so on) and fell back to the backtracking engine, where only the match timeout bounds
/// it. The caller surfaces that fallback as an observability signal; the library itself does no logging.
/// </summary>
public sealed record CompiledRegex
{
    /// <summary>The compiled regex, carrying the requested match timeout and culture-invariant options.</summary>
    public Regex Regex { get; init; }

    /// <summary>Whether the non-backtracking engine accepted the pattern; <c>false</c> means it fell back to backtracking.</summary>
    public bool IsNonBacktracking { get; init; }
}