namespace RaccoonNinja.McpToolset.Server.TextEdit.Errors;

/// <summary>
/// A domain error that travels inside a failure envelope, never through the MCP protocol error
/// channel. It carries a stable <see cref="Code"/> and a <see cref="Detail"/> map. Its message and
/// detail are always built from a static reason plus root-relative values, never from a caught .NET
/// exception's message, so an absolute path can never reach model context.
/// </summary>
public sealed class TextEditException : Exception
{
    /// <summary>Create the exception with a code, a caller-safe message, and optional detail.</summary>
    /// <param name="code">One of <see cref="ErrorCodes"/>.</param>
    /// <param name="message">A caller-facing message with no machine-identifying content.</param>
    /// <param name="detail">Optional structured detail; copied defensively.</param>
    public TextEditException(string code, string message, IDictionary<string, object> detail = null)
        : base(message)
    {
        Code = code;
        Detail = detail is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(detail, StringComparer.Ordinal);
    }

    /// <summary>The stable error code (see <see cref="ErrorCodes"/>).</summary>
    public string Code { get; }

    /// <summary>Structured, machine-privacy-safe detail carried into the failure envelope.</summary>
    public IDictionary<string, object> Detail { get; }

    /// <summary>A contradictory selector (more than one of glob, regex, paths).</summary>
    /// <param name="reason">The plain reason from the selector layer.</param>
    /// <returns>The exception.</returns>
    public static TextEditException SelectorInvalid(string reason)
        => new(ErrorCodes.SelectorInvalid, reason);

    /// <summary>A rejected agent regex.</summary>
    /// <param name="reason">The plain reason from the compiler.</param>
    /// <returns>The exception.</returns>
    public static TextEditException PatternInvalid(string reason)
        => new(ErrorCodes.PatternInvalid, reason);

    /// <summary>An out-of-range or malformed argument.</summary>
    /// <param name="reason">The plain reason.</param>
    /// <returns>The exception.</returns>
    public static TextEditException InvalidArgument(string reason)
        => new(ErrorCodes.InvalidArgument, reason);

    /// <summary>A requested path did not exist or resolved to a directory where a file was required.</summary>
    /// <param name="reason">The plain reason.</param>
    /// <returns>The exception.</returns>
    public static TextEditException NotFound(string reason)
        => new(ErrorCodes.NotFound, reason);

    /// <summary>A supplied path or root escaped the confinement root.</summary>
    /// <param name="reason">The plain reason.</param>
    /// <returns>The exception.</returns>
    public static TextEditException PathOutsideRoot(string reason)
        => new(ErrorCodes.PathOutsideRoot, reason);

    /// <summary>The operation ran past its wall-clock budget.</summary>
    /// <param name="budgetMs">The budget in milliseconds.</param>
    /// <returns>The exception.</returns>
    public static TextEditException OperationBudgetExceeded(int budgetMs)
        => new(
            ErrorCodes.OperationBudgetExceeded,
            $"operation exceeded its {budgetMs} ms budget; narrow the selector or pattern",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["budget_ms"] = budgetMs });

    /// <summary>The rewritable-match count did not equal the caller's expectation; nothing was written.</summary>
    /// <param name="expected">The count the caller asserted.</param>
    /// <param name="actual">The count that would have been rewritten.</param>
    /// <returns>The exception.</returns>
    public static TextEditException ExpectedMatchCountMismatch(int expected, int actual)
        => new(
            ErrorCodes.ExpectedMatchCountMismatch,
            $"expected {expected} match(es) but found {actual}; no files were changed",
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["expected"] = expected,
                ["actual"] = actual,
            });

    /// <summary>The pattern matched nothing in any rewritable file.</summary>
    /// <param name="reason">The plain reason.</param>
    /// <returns>The exception.</returns>
    public static TextEditException NoMatches(string reason)
        => new(ErrorCodes.NoMatches, reason);

    /// <summary>No batch exists for the supplied identifier.</summary>
    /// <param name="batchId">The identifier that did not resolve.</param>
    /// <returns>The exception.</returns>
    public static TextEditException BatchNotFound(long batchId)
        => new(
            ErrorCodes.BatchNotFound,
            $"no batch with id {batchId}",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["batch_id"] = batchId });

    /// <summary>An unexpected internal fault; the reason is a static string, never a caught message.</summary>
    /// <param name="reason">A caller-safe static reason.</param>
    /// <returns>The exception.</returns>
    public static TextEditException Internal(string reason)
        => new(ErrorCodes.InternalError, reason);
}