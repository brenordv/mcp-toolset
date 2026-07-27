using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// A parsed git revision range: <c>Left..Right</c> (two-dot) or <c>Left...Right</c> (three-dot).
/// Either side may be empty, meaning <c>HEAD</c> (git's documented default for an omitted end).
/// Because a git ref name can never contain two consecutive dots, any <c>..</c> run in a ref value
/// is unambiguously a range operator rather than part of a name.
/// </summary>
public readonly record struct RefRange(string Left, string Operator, string Right)
{
    /// <summary>Two-dot operator: commits reachable from <see cref="Right"/> but not <see cref="Left"/>.</summary>
    private const string TwoDot = "..";

    /// <summary>Three-dot operator: symmetric difference (log) / merge-base diff (diff).</summary>
    private const string ThreeDot = "...";

    /// <summary>
    /// Parse <paramref name="reference"/> as a range expression. Returns <c>null</c> when the value
    /// carries no range operator (a plain single ref) and for null/blank input; returns a populated
    /// <see cref="RefRange"/> for a well-formed <c>A..B</c> / <c>A...B</c>. A single dot inside a side
    /// (as in <c>v1.2.3</c>) is not an operator and is preserved.
    /// </summary>
    /// <param name="reference">The candidate ref or range expression.</param>
    /// <returns>The parsed range, or <c>null</c> when the value is not a range.</returns>
    /// <exception cref="RejectedArgumentException">
    /// The value is a malformed range: a dot run longer than three, more than one dot-run operator,
    /// or both sides empty (<c>..</c> / <c>...</c>).
    /// </exception>
    public static RefRange? Parse(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var operatorStart = -1;
        var operatorLength = 0;
        var index = 0;
        while (index < reference.Length)
        {
            if (reference[index] != '.')
            {
                index++;
                continue;
            }

            var runStart = index;
            while (index < reference.Length && reference[index] == '.')
            {
                index++;
            }

            var runLength = index - runStart;
            if (runLength < 2)
            {
                continue;
            }

            if (operatorStart >= 0 || runLength > 3)
            {
                throw MalformedRange();
            }

            operatorStart = runStart;
            operatorLength = runLength;
        }

        if (operatorStart < 0)
        {
            return null;
        }

        var left = reference[..operatorStart];
        var right = reference[(operatorStart + operatorLength)..];
        if (left.Length == 0 && right.Length == 0)
        {
            throw MalformedRange();
        }

        var rangeOperator = operatorLength == 2 ? TwoDot : ThreeDot;
        return new RefRange(left, rangeOperator, right);
    }

    /// <summary>Build the standard malformed-range rejection; the message never echoes the raw value.</summary>
    private static RejectedArgumentException MalformedRange()
        => new("malformed range expression", new Dictionary<string, object> { ["param"] = "ref" });
}