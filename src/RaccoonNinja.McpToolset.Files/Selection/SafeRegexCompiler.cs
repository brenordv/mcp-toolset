using System.Text.RegularExpressions;

namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// Compiles an agent-supplied regex under the ADR-005 guard rails, the single safe entry point both path
/// selection and content search go through. Every pattern gets <see cref="RegexOptions.CultureInvariant"/>
/// (which settles the Turkish-I problem) and the configured match timeout. Compilation is tried first with
/// <see cref="RegexOptions.NonBacktracking"/> for linear-time matching; a pattern using a construct that
/// engine rejects (lookaround, backreference, atomic group, and the like) falls back to the backtracking
/// engine, where the match timeout is the real ReDoS guard. Because the timeout does not bound compilation,
/// the pattern length and the bounded-quantifier product are checked up front, before any <see cref="Regex"/>
/// is built.
/// </summary>
public static class SafeRegexCompiler
{
    /// <summary>Compile <paramref name="pattern"/> under <paramref name="options"/>.</summary>
    /// <param name="pattern">The agent-supplied regex.</param>
    /// <param name="options">The guard rails and case sensitivity.</param>
    /// <returns>The compiled regex plus whether it runs non-backtracking.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pattern"/> or <paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="RegexCompilationException">Thrown when the pattern is too long, has an oversized bounded quantifier, or is not valid regex.</exception>
    public static CompiledRegex Compile(string pattern, SafeRegexOptions options)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(options);

        if (pattern.Length > options.MaxPatternLength)
        {
            throw new RegexCompilationException(
                $"pattern exceeds the {options.MaxPatternLength}-character limit");
        }

        var product = BoundedQuantifierProduct(pattern);
        if (product >= options.MaxRepetitionProduct)
        {
            throw new RegexCompilationException(
                $"pattern's bounded quantifiers reach or exceed the repetition limit of {options.MaxRepetitionProduct}");
        }

        var baseOptions = RegexOptions.CultureInvariant;
        if (!options.CaseSensitive)
        {
            baseOptions |= RegexOptions.IgnoreCase;
        }

        return TryCompile(pattern, baseOptions, options.MatchTimeout);
    }

    /// <summary>Compile with non-backtracking, falling back to backtracking for constructs it cannot express.</summary>
    private static CompiledRegex TryCompile(string pattern, RegexOptions baseOptions, TimeSpan timeout)
    {
        try
        {
            var regex = new Regex(pattern, baseOptions | RegexOptions.NonBacktracking, timeout);
            return new CompiledRegex { Regex = regex, IsNonBacktracking = true };
        }
        catch (NotSupportedException)
        {
            // A valid pattern the non-backtracking engine can't run (lookaround, backreference, ...): fall back.
            return CompileBacktracking(pattern, baseOptions, timeout);
        }
        catch (RegexParseException ex)
        {
            throw new RegexCompilationException($"pattern is not a valid regex: {ex.Message}", ex);
        }
    }

    /// <summary>Compile on the backtracking engine, mapping a parse failure to the typed error.</summary>
    private static CompiledRegex CompileBacktracking(string pattern, RegexOptions baseOptions, TimeSpan timeout)
    {
        try
        {
            var regex = new Regex(pattern, baseOptions, timeout);
            return new CompiledRegex { Regex = regex, IsNonBacktracking = false };
        }
        catch (RegexParseException ex)
        {
            throw new RegexCompilationException($"pattern is not a valid regex: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Multiply the upper bounds of every bounded quantifier (<c>{n}</c>, <c>{n,}</c>, <c>{n,m}</c>) that is a
    /// real quantifier: not escaped and not inside a character class. Overflow saturates to <see cref="long.MaxValue"/>
    /// so a pathological pattern is rejected rather than wrapping around.
    /// </summary>
    private static long BoundedQuantifierProduct(string pattern)
    {
        long product = 1;
        var inClass = false;
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (inClass)
            {
                if (c == ']')
                {
                    inClass = false;
                }

                i++;
                continue;
            }

            if (c == '[')
            {
                inClass = true;
                i++;
                continue;
            }

            if (c == '{' && TryReadQuantifierBound(pattern, i, out var upper, out var next))
            {
                product = SaturatingMultiply(product, upper);
                i = next;
                continue;
            }

            i++;
        }

        return product;
    }

    /// <summary>Parse <c>{n}</c>/<c>{n,}</c>/<c>{n,m}</c> at <paramref name="open"/>, reporting its upper bound and the index past it.</summary>
    private static bool TryReadQuantifierBound(string s, int open, out long upper, out int next)
    {
        upper = 0;
        next = open;

        var j = open + 1;
        if (!TryReadNumber(s, ref j, out var min))
        {
            return false;
        }

        if (j < s.Length && s[j] == '}')
        {
            upper = min;
            next = j + 1;
            return true;
        }

        if (j >= s.Length || s[j] != ',')
        {
            return false;
        }

        j++;
        if (j < s.Length && s[j] == '}')
        {
            // {n,} is unbounded above; use the lower bound as the weight so it still contributes to the product.
            upper = min;
            next = j + 1;
            return true;
        }

        if (!TryReadNumber(s, ref j, out var max) || j >= s.Length || s[j] != '}')
        {
            return false;
        }

        upper = max;
        next = j + 1;
        return true;
    }

    /// <summary>Read a run of ASCII digits into <paramref name="value"/>, saturating at <see cref="long.MaxValue"/>.</summary>
    private static bool TryReadNumber(string s, ref int i, out long value)
    {
        var start = i;
        long acc = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            acc = SaturatingMultiply(acc, 10);
            if (acc != long.MaxValue)
            {
                acc += s[i] - '0';
            }

            i++;
        }

        value = acc;
        return i > start;
    }

    private static long SaturatingMultiply(long a, long b)
    {
        if (a == 0 || b == 0)
        {
            return 0;
        }

        return a > long.MaxValue / b ? long.MaxValue : a * b;
    }
}