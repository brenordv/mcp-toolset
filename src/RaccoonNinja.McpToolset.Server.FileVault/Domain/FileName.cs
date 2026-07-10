using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Domain;

/// <summary>
/// A validated single-segment file name (no <c>/</c>), matching <c>[A-Za-z0-9._-]</c> and never
/// <c>.</c>/<c>..</c>. This type is the only sanctioned way a caller-supplied name becomes an
/// on-disk path component.
/// </summary>
public sealed record FileName
{
    private FileName(string value)
    {
        Value = value;
    }

    /// <summary>The validated name.</summary>
    public string Value { get; }

    /// <summary>Parse and validate a raw file name.</summary>
    /// <param name="value">The caller-supplied name.</param>
    /// <returns>The validated name.</returns>
    /// <exception cref="VaultException">Thrown with <see cref="VaultErrorCode.InvalidName"/> when validation fails.</exception>
    public static FileName Parse(string value)
    {
        if (value is not null && value.Length > NameValidation.MaxNameLength)
        {
            throw VaultException.InvalidName(
                $"file name is longer than {NameValidation.MaxNameLength} characters");
        }

        return NameValidation.IsValidSegment(value)
            ? new FileName(value)
            : throw VaultException.InvalidName(
                $"file name '{value}' must be a single segment of [A-Za-z0-9._-], not '.'/'..', "
                + "and contain no path separators");
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}