using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Domain;

/// <summary>
/// A validated project namespace: one or more <c>[A-Za-z0-9._-]</c> segments joined by <c>/</c>
/// (e.g. <c>vault-mcp</c> or <c>monorepo/api</c>). Each segment is individually validated, so
/// traversal is impossible by construction. The canonical form always uses <c>/</c>, never OS
/// separators.
/// </summary>
public sealed record ProjectName
{
    private ProjectName(string value)
    {
        Value = value;
    }

    /// <summary>The canonical <c>/</c>-joined project string.</summary>
    public string Value { get; }

    /// <summary>The individual path segments, in order.</summary>
    public IReadOnlyList<string> Segments => Value.Split('/');

    /// <summary>Parse and validate a raw project string.</summary>
    /// <param name="value">The caller-supplied project.</param>
    /// <returns>The validated project.</returns>
    /// <exception cref="VaultException">Thrown with <see cref="VaultErrorCode.InvalidName"/> when validation fails.</exception>
    public static ProjectName Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw VaultException.InvalidName("project name is empty");
        }

        if (value.Length > NameValidation.MaxProjectLength)
        {
            throw VaultException.InvalidName(
                $"project is longer than {NameValidation.MaxProjectLength} characters");
        }

        var segments = value.Split('/');
        if (segments.Length > NameValidation.MaxProjectSegments)
        {
            throw VaultException.InvalidName(
                $"project has more than {NameValidation.MaxProjectSegments} segments");
        }

        if (!segments.All(NameValidation.IsValidSegment))
        {
            throw VaultException.InvalidName(
                $"project '{value}' must be one or more '/'-joined segments of [A-Za-z0-9._-], "
                + "with no '.' or '..' segment");
        }

        return new ProjectName(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}