using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git for-each-ref</c> output printed with <see cref="FormatFlag"/>: each field ended by NUL, each ref then
/// ended by the newline git prints after it (see <see cref="NulRecords.SplitNewlineTerminated"/>).
/// </summary>
public static class BranchParser
{
    /// <summary>
    /// The format flag this parser expects: the refname, the HEAD marker, the object name, the short upstream, and the tip
    /// subject, each followed by <c>%00</c>.
    /// </summary>
    public const string FormatFlag =
        "--format=%(refname)%00%(HEAD)%00%(objectname)%00%(upstream:short)%00%(contents:subject)%00";

    private const int FieldCount = 5;
    private const int SubjectField = 4;
    private const string HeadsPrefix = "refs/heads/";
    private const string RemotesPrefix = "refs/remotes/";

    /// <summary>
    /// Parse the output of a <c>git for-each-ref</c> run with <see cref="FormatFlag"/>. When the output was cut by the
    /// cap, its last ref is left out unless that ref's subject is non-empty: a non-empty subject shows the four fields
    /// before it are whole, while a ref cut earlier may carry a cut name, object id, or upstream, and git reads such a
    /// value as a ref name before it reads it as an abbreviated id, so it could name another ref. A whole last ref with
    /// an empty subject is left out of a cut output too.
    /// </summary>
    /// <param name="raw">Raw stdout bytes from <c>git for-each-ref</c>.</param>
    /// <param name="truncated">Whether the output hit the output cap.</param>
    /// <returns>The parsed branches, or an empty list when there is nothing to parse.</returns>
    /// <exception cref="GitCommandException">
    /// The output is not a whole number of refs; output the cap cut may end only inside or right after its last ref.
    /// </exception>
    public static IReadOnlyList<Branch> Parse(byte[] raw, bool truncated)
    {
        var records = NulRecords.SplitNewlineTerminated(raw, FieldCount, truncated);
        var cutBeforeSubject = truncated && records.Count > 0 && records[^1][SubjectField].Length == 0;
        return [.. records.Take(cutBeforeSubject ? records.Count - 1 : records.Count).Select(ToBranch)];
    }

    /// <summary>Build a <see cref="Branch"/> from one ref's fields.</summary>
    /// <param name="fields">The ref's fields, in <see cref="FormatFlag"/> order.</param>
    /// <returns>The branch.</returns>
    private static Branch ToBranch(string[] fields)
    {
        var refname = fields[0];
        var isRemote = refname.StartsWith(RemotesPrefix, StringComparison.Ordinal);
        var name = refname;
        if (refname.StartsWith(HeadsPrefix, StringComparison.Ordinal))
        {
            name = refname[HeadsPrefix.Length..];
        }
        else if (isRemote)
        {
            name = refname[RemotesPrefix.Length..];
        }

        return new Branch
        {
            Name = name,
            IsCurrent = fields[1] == "*",
            IsRemote = isRemote,
            Upstream = string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3],
            TipHash = fields[2],
            Subject = string.IsNullOrWhiteSpace(fields[SubjectField]) ? null : fields[SubjectField],
        };
    }
}