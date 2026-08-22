namespace RaccoonNinja.McpToolset.Server.SkillStats.Envelope;

/// <summary>
/// Builds the <c>filters_applied</c> map. Only safe scalars are echoed: an integer window/limit and a
/// resolved skill name (a skill identifier, never a path). Nothing machine-identifying is recorded.
/// </summary>
public sealed class FiltersAppliedBuilder
{
    private readonly Dictionary<string, object> _map = new(StringComparer.Ordinal);

    /// <summary>Start a new builder.</summary>
    /// <returns>The builder.</returns>
    public static FiltersAppliedBuilder Create() => new();

    /// <summary>Record a non-sensitive string value verbatim (for example a resolved skill name).</summary>
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

    /// <summary>Record an integer verbatim.</summary>
    /// <param name="key">The field name.</param>
    /// <param name="value">The integer value.</param>
    /// <returns>The builder.</returns>
    public FiltersAppliedBuilder Number(string key, int value)
    {
        _map[key] = value;
        return this;
    }

    /// <summary>Return the accumulated map.</summary>
    /// <returns>The filters map.</returns>
    public IDictionary<string, object> Build() => _map;
}