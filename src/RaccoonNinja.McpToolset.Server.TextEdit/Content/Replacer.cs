using System.Text.RegularExpressions;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// Substitutes a literal or regex pattern across a file's decoded text. A regex is compiled once through
/// <see cref="SafeRegexCompiler"/> (culture-invariant, timeout-guarded, length- and repetition-capped) and
/// substituted with .NET's native back-reference semantics, so <c>$1</c>, <c>${name}</c>, and <c>$$</c>
/// carry through without a custom substitution engine. The match count it reports is what the batch tallies
/// for an <c>expected_match_count</c> guard, counted only in files the gate will actually rewrite.
/// </summary>
public sealed class Replacer : ITextTransform
{
    private readonly bool _isRegex;
    private readonly bool _caseSensitive;
    private readonly string _pattern;
    private readonly string _replacement;
    private readonly Regex _regex;

    /// <summary>Create a replacer, compiling the regex up front when <paramref name="isRegex"/> is set.</summary>
    /// <param name="pattern">The literal string or regex to match.</param>
    /// <param name="replacement">The replacement (regex back-references apply only when <paramref name="isRegex"/>).</param>
    /// <param name="isRegex">Whether <paramref name="pattern"/> is a regex.</param>
    /// <param name="caseSensitive">Whether matching is case-sensitive.</param>
    /// <param name="config">The server config, supplying the regex timeout and pattern-length cap.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="pattern"/> is empty.</exception>
    /// <exception cref="RegexCompilationException">Thrown when a regex pattern fails the safety guards.</exception>
    public Replacer(string pattern, string replacement, bool isRegex, bool caseSensitive, EditConfig config)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(config);

        _pattern = pattern;
        _replacement = replacement;
        _isRegex = isRegex;
        _caseSensitive = caseSensitive;
        IsNonBacktracking = true;

        if (isRegex)
        {
            var compiled = SafeRegexCompiler.Compile(pattern, new SafeRegexOptions
            {
                CaseSensitive = caseSensitive,
                MatchTimeout = config.RegexTimeout,
                MaxPatternLength = config.PatternLengthCap,
            });
            _regex = compiled.Regex;
            IsNonBacktracking = compiled.IsNonBacktracking;
        }
    }

    /// <summary>Whether a regex pattern ran on the linear-time engine; <c>false</c> means it fell back to backtracking.</summary>
    public bool IsNonBacktracking { get; }

    /// <inheritdoc />
    /// <exception cref="RegexMatchTimeoutException">Thrown when a regex substitution exceeds its per-match timeout.</exception>
    public TransformResult Transform(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int count;
        string newText;
        if (_isRegex)
        {
            count = _regex.Matches(text).Count;
            newText = count == 0 ? text : _regex.Replace(text, _replacement);
        }
        else
        {
            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            count = CountOccurrences(text, _pattern, comparison);
            newText = count == 0 ? text : text.Replace(_pattern, _replacement, comparison);
        }

        return new TransformResult { NewText = newText, MatchCount = count };
    }

    private static int CountOccurrences(string text, string pattern, StringComparison comparison)
    {
        var count = 0;
        var index = text.IndexOf(pattern, comparison);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(pattern, index + pattern.Length, comparison);
        }

        return count;
    }
}