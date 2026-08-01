namespace RaccoonNinja.McpToolset.Server.TextSearch.Errors;

/// <summary>
/// A domain error that travels inside a failure envelope, never through the MCP protocol error
/// channel. It carries a stable <see cref="Code"/> and a <see cref="Detail"/> map. Its message and
/// detail are always built from a static reason plus root-relative values, never from a caught .NET
/// exception's message, so an absolute path can never reach model context on the blanket-approved
/// server.
/// </summary>
public sealed class TextSearchException : Exception
{
    /// <summary>Create the exception with a code, a caller-safe message, and optional detail.</summary>
    /// <param name="code">One of <see cref="ErrorCodes"/>.</param>
    /// <param name="message">A caller-facing message with no machine-identifying content.</param>
    /// <param name="detail">Optional structured detail; copied defensively.</param>
    /// <param name="refusalReason">
    /// When set, marks this error as a boundary refusal so it is counted in <c>refusals_total</c> (for
    /// example <c>cwd_outside_base</c>); <c>null</c> for an ordinary error.
    /// </param>
    public TextSearchException(string code, string message, IDictionary<string, object> detail = null, string refusalReason = null)
        : base(message)
    {
        Code = code;
        RefusalReason = refusalReason;
        Detail = detail is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(detail, StringComparer.Ordinal);
    }

    /// <summary>The stable error code (see <see cref="ErrorCodes"/>).</summary>
    public string Code { get; }

    /// <summary>The boundary-refusal reason when this error is a refusal (counted in <c>refusals_total</c>); otherwise <c>null</c>.</summary>
    public string RefusalReason { get; }

    /// <summary>Structured, machine-privacy-safe detail carried into the failure envelope.</summary>
    public IDictionary<string, object> Detail { get; }

    /// <summary>A contradictory selector (more than one of glob, regex, paths).</summary>
    /// <param name="reason">The plain reason from the selector layer.</param>
    /// <returns>The exception.</returns>
    public static TextSearchException SelectorInvalid(string reason)
        => new(ErrorCodes.SelectorInvalid, reason);

    /// <summary>A rejected agent regex.</summary>
    /// <param name="reason">The plain reason from the compiler.</param>
    /// <returns>The exception.</returns>
    public static TextSearchException PatternInvalid(string reason)
        => new(ErrorCodes.PatternInvalid, reason);

    /// <summary>An out-of-range or malformed argument.</summary>
    /// <param name="reason">The plain reason.</param>
    /// <returns>The exception.</returns>
    public static TextSearchException InvalidArgument(string reason)
        => new(ErrorCodes.InvalidArgument, reason);

    /// <summary>The operation ran past its wall-clock budget.</summary>
    /// <param name="budgetMs">The budget in milliseconds.</param>
    /// <returns>The exception.</returns>
    public static TextSearchException OperationBudgetExceeded(int budgetMs)
        => new(
            ErrorCodes.OperationBudgetExceeded,
            $"operation exceeded its {budgetMs} ms budget; narrow the selector or pattern",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["budget_ms"] = budgetMs });
}