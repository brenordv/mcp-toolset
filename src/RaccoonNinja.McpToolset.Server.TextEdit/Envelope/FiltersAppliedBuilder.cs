namespace RaccoonNinja.McpToolset.Server.TextEdit.Envelope;

/// <summary>
/// Builds the <c>filters_applied</c> map so a user-controlled string can never be echoed verbatim.
/// A selector's glob/regex/paths/pattern go through <see cref="Redact"/> (recorded as a placeholder,
/// present only when the caller supplied one); only safe scalars (bools, counts) are echoed as-is.
/// </summary>
public sealed class FiltersAppliedBuilder
{
    /// <summary>The placeholder recorded in place of a redacted value.</summary>
    public const string RedactedToken = "<provided>";

    private readonly Dictionary<string, object> _map = new(StringComparer.Ordinal);

    /// <summary>Start a new builder.</summary>
    /// <returns>The builder.</returns>
    public static FiltersAppliedBuilder Create() => new();

    /// <summary>Record that <paramref name="key"/> was supplied, without echoing its value.</summary>
    /// <param name="key">The field name.</param>
    /// <param name="value">The user value; the key is omitted when this is null or blank.</param>
    /// <returns>The builder.</returns>
    public FiltersAppliedBuilder Redact(string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _map[key] = RedactedToken;
        }

        return this;
    }

    /// <summary>Record a non-sensitive string value verbatim (for example a resolved root name).</summary>
    /// <param name="key">The field name.</param>
    /// <param name="value">The value; the key is omitted when this is null or blank.</param>
    /// <returns>The builder.</returns>
    public FiltersAppliedBuilder Value(string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _map[key] = value;
        }

        return this;
    }

    /// <summary>Record a boolean flag verbatim.</summary>
    /// <param name="key">The field name.</param>
    /// <param name="value">The flag value.</param>
    /// <returns>The builder.</returns>
    public FiltersAppliedBuilder Flag(string key, bool value)
    {
        _map[key] = value;
        return this;
    }

    /// <summary>Record an integer verbatim.</summary>
    /// <param name="key">The field name.</param>
    /// <param name="value">The integer value.</param>
    /// <returns>The builder.</returns>
    public FiltersAppliedBuilder Number(string key, int value)
    {
        _map[key] = value;
        return this;
    }

    /// <summary>Record how many explicit paths were supplied, without echoing them.</summary>
    /// <param name="key">The field name.</param>
    /// <param name="count">The number of paths.</param>
    /// <returns>The builder.</returns>
    public FiltersAppliedBuilder Count(string key, int count)
    {
        if (count > 0)
        {
            _map[key] = count;
        }

        return this;
    }

    /// <summary>Return the accumulated map.</summary>
    /// <returns>The filters map.</returns>
    public IDictionary<string, object> Build() => _map;
}