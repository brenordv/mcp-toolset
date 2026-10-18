namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// Recognizes git's <c>&lt;rev&gt;:&lt;path&gt;</c> and <c>:&lt;path&gt;</c> revision forms, which name a
/// blob or tree rather than a commit. A ref name can never contain a colon, so a colon outside
/// <c>@{...}</c> (reflog date) and <c>^{...}</c> (peel or message search) always introduces a path;
/// the one exception is a leading <c>:/text</c>, which names a commit by its message.
/// </summary>
public static class RevPathExpression
{
    /// <summary>Whether <paramref name="reference"/> is a <c>&lt;rev&gt;:&lt;path&gt;</c> or <c>:&lt;path&gt;</c> expression.</summary>
    /// <param name="reference">The ref argument as the caller passed it.</param>
    /// <returns><c>true</c> for a path expression; <c>false</c> for a plain commit-ish, blank input, or an unterminated brace (left to <c>rev-parse</c>).</returns>
    public static bool IsRevPath(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var i = 0;
        while (i < reference.Length)
        {
            var c = reference[i];
            if ((c == '@' || c == '^') && i + 1 < reference.Length && reference[i + 1] == '{')
            {
                var close = reference.IndexOf('}', i + 2);
                if (close < 0)
                {
                    return false;
                }

                i = close + 1;
                continue;
            }

            if (c == ':')
            {
                var isCommitSearch = i == 0 && i + 1 < reference.Length && reference[i + 1] == '/';
                return !isCommitSearch;
            }

            i++;
        }

        return false;
    }
}