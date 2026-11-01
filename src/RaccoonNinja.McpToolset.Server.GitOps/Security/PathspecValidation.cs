using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Shape checks for pathspec values that git matches as patterns (wildcards and excludes). These values
/// are not filesystem paths, so they bypass <see cref="PathConfinement"/>; git only ever matches a
/// pathspec against tracked entries, and these checks reject the shapes that would be errors or
/// surprises rather than confine anything.
/// </summary>
public static class PathspecValidation
{
    private const string PathsParam = "paths";

    /// <summary>Validate a wildcard or exclude pathspec value and return it unchanged.</summary>
    /// <param name="paramName">The tool parameter the value came from, for the message and the error detail.</param>
    /// <param name="value">The user-supplied pattern.</param>
    /// <returns><paramref name="value"/> unchanged.</returns>
    /// <exception cref="RejectedArgumentException">The value is empty, starts with ':' or '-', uses a backslash, is absolute or drive-prefixed, or contains a '..' segment.</exception>
    public static string ValidatePattern(string paramName, string value)
    {
        ArgumentValidation.RejectIfUnsafeValue(paramName, value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw Rejected($"{paramName} entry must not be empty", paramName);
        }

        if (value[0] == ':')
        {
            throw Rejected($"{paramName} entry must not use pathspec magic (a leading ':')", paramName);
        }

        if (value.Contains('\\'))
        {
            throw Rejected($"{paramName} entry must use '/' as the separator", paramName);
        }

        if (IsAbsoluteOrDrivePrefixed(value))
        {
            throw Rejected($"{paramName} entry must be repo-relative", paramName);
        }

        if (value.Split('/').Contains("..", StringComparer.Ordinal))
        {
            throw Rejected($"{paramName} entry must not contain '..'", paramName);
        }

        return value;
    }

    /// <summary>Reject a <c>paths</c> entry that carries wildcard characters or pathspec magic; return it unchanged otherwise.</summary>
    /// <param name="value">The raw <c>paths</c> entry, before confinement.</param>
    /// <returns><paramref name="value"/> unchanged.</returns>
    /// <exception cref="RejectedArgumentException">The value is empty, contains '*' or '?', or starts with ':'.</exception>
    public static string EnsureLiteral(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Rejected("paths entry must not be empty", PathsParam);
        }

        if (value.Contains('*') || value.Contains('?') || value[0] == ':')
        {
            throw Rejected(
                "paths entries are literal repo-relative paths; wildcards (* ?) and pathspec magic (a leading ':') are rejected",
                PathsParam);
        }

        return value;
    }

    /// <summary>True when <paramref name="value"/> starts with '/' (which also covers a leading '//') or a drive prefix such as <c>C:</c>.</summary>
    private static bool IsAbsoluteOrDrivePrefixed(string value)
        => value[0] == '/' || (value.Length >= 2 && value[1] == ':' && char.IsAsciiLetter(value[0]));

    private static RejectedArgumentException Rejected(string message, string param)
        => new(message, new Dictionary<string, object> { ["param"] = param });
}