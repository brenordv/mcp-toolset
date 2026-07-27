namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// Thrown when an agent-supplied pattern is rejected before or during compilation: it is too long, carries
/// an oversized bounded quantifier, or is not valid regex on either engine. It carries a plain reason with
/// no machine-identifying detail so a server can map it straight into a typed tool error.
/// </summary>
public sealed class RegexCompilationException : Exception
{
    /// <summary>Create the exception with a caller-facing <paramref name="reason"/>.</summary>
    /// <param name="reason">Why the pattern was rejected.</param>
    public RegexCompilationException(string reason)
        : base(reason)
    {
    }

    /// <summary>Create the exception with a <paramref name="reason"/> and the underlying compile failure.</summary>
    /// <param name="reason">Why the pattern was rejected.</param>
    /// <param name="innerException">The underlying regex failure.</param>
    public RegexCompilationException(string reason, Exception innerException)
        : base(reason, innerException)
    {
    }
}