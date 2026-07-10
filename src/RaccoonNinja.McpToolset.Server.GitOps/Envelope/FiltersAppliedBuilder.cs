namespace RaccoonNinja.McpToolset.Server.GitOps.Envelope;

/// <summary>
/// Builds the <c>filters_applied</c> map for a <see cref="ResultEnvelope"/> using a single,
/// security-safe convention: user-controlled string values are always redacted, safe scalars
/// (booleans, counts) are reported verbatim, and unset optional values are omitted.
/// </summary>
/// <remarks>
/// Centralizing this prevents the per-tool drift where some tools emitted explicit nulls and
/// others omitted keys and, critically, makes it impossible to forget redacting a raw value:
/// the only way to add a string is through <see cref="Redact"/>.
/// </remarks>
public sealed class FiltersAppliedBuilder
{
    /// <summary>The placeholder emitted in place of any user-controlled string value.</summary>
    public const string RedactedToken = "<redacted>";

    private readonly Dictionary<string, object> _filters = new(StringComparer.Ordinal);

    /// <summary>Start a new, empty builder.</summary>
    public static FiltersAppliedBuilder Create() => new();

    /// <summary>
    /// Record a user-controlled string filter as <see cref="RedactedToken"/>. The key is added
    /// only when <paramref name="value"/> was actually supplied, so the raw value never escapes.
    /// </summary>
    /// <param name="key">The filter name.</param>
    /// <param name="value">The user-supplied value; ignored when null or empty.</param>
    /// <returns>This builder, for chaining.</returns>
    public FiltersAppliedBuilder Redact(string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _filters[key] = RedactedToken;
        }

        return this;
    }

    /// <summary>Record a safe boolean toggle with its actual value.</summary>
    /// <param name="key">The filter name.</param>
    /// <param name="value">The toggle state.</param>
    /// <returns>This builder, for chaining.</returns>
    public FiltersAppliedBuilder Flag(string key, bool value)
    {
        _filters[key] = value;
        return this;
    }

    /// <summary>Record a safe integer/count filter with its actual value.</summary>
    /// <param name="key">The filter name.</param>
    /// <param name="value">The numeric value.</param>
    /// <returns>This builder, for chaining.</returns>
    public FiltersAppliedBuilder Number(string key, int value)
    {
        _filters[key] = value;
        return this;
    }

    /// <summary>Record an optional safe value, included only when <paramref name="value"/> is non-null.</summary>
    /// <param name="key">The filter name.</param>
    /// <param name="value">The value to include, or null to omit the key.</param>
    /// <returns>This builder, for chaining.</returns>
    public FiltersAppliedBuilder Optional(string key, object value)
    {
        if (value is not null)
        {
            _filters[key] = value;
        }

        return this;
    }

    /// <summary>Materialize the accumulated filter map.</summary>
    /// <returns>The filters to attach to a <see cref="ResultEnvelope"/>.</returns>
    public Dictionary<string, object> Build() => _filters;
}